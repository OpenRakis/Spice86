namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Reflection;

using CfgSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>
/// Turns each execution-AST node into the C# code that replaces it. Pure expressions and simple statements
/// (registers, memory accesses, arithmetic, assignments, while loops, returns) lower directly from the node's
/// own syntax; control-flow nodes (jumps, calls, returns, self-modifying selectors) become concrete
/// <c>goto</c>s, helper calls, and dispatch switches using the CFG generation context. The transfer mechanics
/// themselves are delegated to <see cref="TransferEmitter"/>.
/// </summary>
internal sealed class CSharpAstEmitter : IAstVisitor<EmittedCode> {
    private const int AssignmentPrecedence = 1;
    private const int LogicalOrPrecedence = 2;
    private const int LogicalAndPrecedence = 3;
    private const int BitwiseOrPrecedence = 4;
    private const int BitwiseXorPrecedence = 5;
    private const int BitwiseAndPrecedence = 6;
    private const int EqualityPrecedence = 7;
    private const int RelationalPrecedence = 8;
    private const int ShiftPrecedence = 9;
    private const int AdditivePrecedence = 10;
    private const int MultiplicativePrecedence = 11;
    private const int UnaryPrecedence = 12;
    private const int CastPrecedence = 12;

    private static readonly Dictionary<Type, ClrTypeInfo> ClrTypesByType = new() {
        [typeof(byte)] = new ClrTypeInfo("byte", DataType.UINT8),
        [typeof(sbyte)] = new ClrTypeInfo("sbyte", DataType.INT8),
        [typeof(ushort)] = new ClrTypeInfo("ushort", DataType.UINT16),
        [typeof(short)] = new ClrTypeInfo("short", DataType.INT16),
        [typeof(uint)] = new ClrTypeInfo("uint", DataType.UINT32),
        [typeof(int)] = new ClrTypeInfo("int", DataType.INT32),
        [typeof(ulong)] = new ClrTypeInfo("ulong", DataType.UINT64),
        [typeof(long)] = new ClrTypeInfo("long", DataType.INT64),
        [typeof(bool)] = new ClrTypeInfo("bool", null)
    };

    private readonly RegisterRenderer _registerRenderer = new(AsmRenderingConfig.CreateSpice86Style());
    private string _localVariableSuffix = string.Empty;
    private MethodPlan? _currentMethod;

    /// <summary>
    /// Constructs an emitter wired with the CFG-aware services it consults when lowering control-flow nodes:
    /// the generation context and the transfer-lowering service.
    /// </summary>
    public CSharpAstEmitter(CfgGeneratorContext context, TransferEmitter transferEmitter) {
        Context = context;
        Transfer = transferEmitter;
    }

    private CfgGeneratorContext Context { get; }

    private TransferEmitter Transfer { get; }

    private MethodPlan CurrentMethod => _currentMethod
        ?? throw new InvalidOperationException("No current method is set; call SetCurrentMethod before lowering control-flow nodes.");

    /// <summary>
    /// Sets the partition method whose body is currently being lowered. The control-flow lowering consults it
    /// (label resolution, goto-vs-fallthrough decisions, next-emitted-node ordering).
    /// </summary>
    public void SetCurrentMethod(MethodPlan method) => _currentMethod = method;

    /// <summary>
    /// Sets the address suffix appended to local variable declarations/references emitted for the current
    /// instruction, so temps of the same name declared by different instructions in one method body do not
    /// collide.
    /// </summary>
    public void SetCurrentInstructionAddress(SegmentedAddress address) {
        _localVariableSuffix = $"{address.Segment:X4}_{address.Offset:X4}_{address.Linear:X5}";
    }

    // ----------------------------------------------------------------------------------------------------
    // Per-instruction driving entry. The lowering itself is uniform Accept dispatch; this method only adds
    // the instruction-level fallthrough transfer (the one inter-instruction concern the AST node cannot see).
    // ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lowers the body of an instruction whose execution AST is rooted at <paramref name="node"/> through the
    /// AST's own <c>Accept</c> dispatch, then appends the fallthrough transfer to the instruction's unique
    /// successor unless the body already terminates control flow.
    /// </summary>
    public EmittedCode LowerInstructionBody(CfgInstruction instruction, IVisitableAstNode node) {
        EmittedCode body = node.Accept(this);
        // Skip the fallthrough transfer when the node owns its control flow, or when the lowered body already
        // diverges (e.g. a CPUID/throw node lowering to a `throw`): appending a fallthrough after a diverging
        // body would be unreachable code (CS0162).
        if (TerminatesControlFlow(node) || !body.CompletesNormally) {
            return body;
        }
        return EmittedCode.Concat(body, Transfer.EmitFallthroughIfNeeded(instruction, CurrentMethod));
    }

    private static bool TerminatesControlFlow(IVisitableAstNode node) =>
        NodeContainsControlFlow(node) || node is InvalidInstructionNode;

    private static bool NodeContainsControlFlow(IVisitableAstNode node) => node switch {
        BlockNode blockNode => blockNode.Statements.Any(NodeContainsControlFlow),
        IfElseNode ifElseNode => NodeContainsControlFlow(ifElseNode.TrueCase) || NodeContainsControlFlow(ifElseNode.FalseCase),
        JumpNearNode or JumpFarNode or CallNearNode or CallFarNode or InterruptCallNode or CallbackNode
            or ReturnNearNode or ReturnFarNode or ReturnInterruptNode => true,
        _ => false
    };

    // ----------------------------------------------------------------------------------------------------
    // Control-flow visit methods. These consult the CFG generation context and the transfer emitter.
    // ----------------------------------------------------------------------------------------------------

    public EmittedCode VisitJumpNearNode(JumpNearNode node) {
        if (TryGetConstantWord(node.Ip) is ushort offset) {
            return Transfer.Emit(Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
                new SegmentedAddress(node.Instruction.Address.Segment, offset)), CurrentMethod);
        }

        return BuildNearRuntimeDispatch(node.Instruction, Expr(node.Ip), "jump",
            edge => Transfer.Emit(edge, CurrentMethod).AsStatements());
    }

    public EmittedCode VisitJumpFarNode(JumpFarNode node) {
        ushort? segment = TryGetConstantWord(node.TargetAddress.Segment);
        ushort? offset = TryGetConstantWord(node.TargetAddress.Offset);
        if (segment is not null && offset is not null) {
            return Transfer.Emit(Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
                new SegmentedAddress(segment.Value, offset.Value)), CurrentMethod);
        }

        return BuildFarRuntimeDispatch(node.Instruction, Expr(node.TargetAddress.Segment), Expr(node.TargetAddress.Offset), "jump",
            // forceSameMethodGoto: far dispatch is a flat chain of sibling `if`s with the untested-target
            // throw as the next sibling (unlike the near switch, whose `break` exits past the construct to
            // the next node). An empty matched branch would fall through the remaining checks into that
            // throw, so the adjacency-fallthrough optimization cannot apply here.
            edge => Transfer.Emit(edge, CurrentMethod, forceSameMethodGoto: true).AsStatements());
    }

    public EmittedCode VisitCallNearNode(CallNearNode node) {
        string helperName = node.CallBitWidth == BitWidth.WORD_16 ? "NearCall" : "NearCall32";
        if (TryGetConstantWord(node.TargetIp) is not ushort targetOffset) {
            CallContinuation runtimeContinuation = Context.ResolveCallContinuation(node.Instruction);
            return BuildNearRuntimeDispatch(node.Instruction, Expr(node.TargetIp), "call",
                edge => CallBranchBody(node.Instruction, runtimeContinuation, helperName, edge, isFarCall: false, forceSameMethodGoto: false));
        }

        ResolvedCfgEdge targetEdge = Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
            new SegmentedAddress(node.Instruction.Address.Segment, targetOffset));
        return Transfer.EmitCallHelperAndContinuation(helperName, node.Instruction, Transfer.FunctionExpression(targetEdge), CurrentMethod);
    }

    public EmittedCode VisitCallFarNode(CallFarNode node) {
        string helperName = node.CallBitWidth == BitWidth.WORD_16 ? "FarCall" : "FarCall32";
        ushort? targetSegment = TryGetConstantWord(node.TargetAddress.Segment);
        ushort? targetOffset = TryGetConstantWord(node.TargetAddress.Offset);
        if (targetSegment is null || targetOffset is null) {
            CallContinuation runtimeContinuation = Context.ResolveCallContinuation(node.Instruction);
            return BuildFarRuntimeDispatch(node.Instruction, Expr(node.TargetAddress.Segment), Expr(node.TargetAddress.Offset), "call",
                edge => CallBranchBody(node.Instruction, runtimeContinuation, helperName, edge, isFarCall: true, forceSameMethodGoto: true));
        }

        ResolvedCfgEdge targetEdge = Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
            new SegmentedAddress(targetSegment.Value, targetOffset.Value));
        return Transfer.EmitCallHelperAndContinuation(helperName, node.Instruction, Transfer.FunctionExpression(targetEdge), CurrentMethod, farCallTargetCs: targetSegment.Value);
    }

    public EmittedCode VisitInterruptCallNode(InterruptCallNode node) {
        CallContinuation continuation = Context.ResolveCallContinuation(node.Instruction);
        SegmentedAddress expectedReturn = continuation.ExpectedReturnAddress;
        return EmittedCode.Concat(
            EmittedCode.Line($"InterruptCall({Context.GetSegmentVariable(expectedReturn.Segment)}, 0x{expectedReturn.Offset:X4}, unchecked((byte)({Expr(node.VectorNumber)})));"),
            Transfer.EmitPostCallContinuation(node.Instruction, continuation, CurrentMethod));
    }

    public EmittedCode VisitCallbackNode(CallbackNode node) =>
        EmittedCode.Concat(
            EmittedCode.Line($"Callback(unchecked((ushort)({Expr(node.CallbackNumber)})));"),
            Transfer.EmitFallthroughIfNeeded(node.Instruction, CurrentMethod));

    public EmittedCode VisitSelectorNode(SelectorNode node) => LowerSelector(node.CfgSelector);

    /// <summary>
    /// Lowers a self-modifying-code selector. Branches are ordered to match the interpreter oracle
    /// (<see cref="CfgSelectorNode.GetNextSuccessor"/>), which iterates signatures in
    /// <see cref="Signature"/> order (ascending length first, see <see cref="Signature.CompareTo"/>) and
    /// takes the <em>first</em> equivalent match. Because a <c>null</c> signature byte is a wildcard, two
    /// signatures of different lengths can both match the same memory, so the generated branch order must be
    /// the same ascending order to pick the same variant as the interpreter. If no signature matches the
    /// generated code fails as untested.
    /// </summary>
    private EmittedCode LowerSelector(CfgSelectorNode selectorNode) {
        List<StatementItem> items = [];
        foreach ((Signature signature, CfgInstruction target) in selectorNode.SuccessorsPerSignature
                     .OrderBy(entry => entry.Key)) {
            string condition = BuildSignatureCondition(selectorNode, signature);
            ResolvedCfgEdge edge = new(selectorNode, target, InstructionSuccessorType.Normal,
                Context.FindTransfer(selectorNode, target, InstructionSuccessorType.Normal)?.Kind);
            items.Add(new BlockStatement($"if ({condition})", Transfer.Emit(edge, CurrentMethod, forceSameMethodGoto: true).AsStatements()));
        }
        items.Add(new LineStatement($"throw FailAsUntested(\"No selector signature matched at {selectorNode.Address}\");", Diverges: true));
        return EmittedCode.Statements(items);
    }

    private string BuildSignatureCondition(CfgSelectorNode selectorNode, Signature signature) {
        string signatureBytes = string.Join(", ", signature.SignatureValue.Select(value => value is byte byteValue ? $"(byte)0x{byteValue:X2}" : "null"));
        return $"SelectorSignatureMatches({Context.GetSegmentVariable(selectorNode.Address.Segment)}, 0x{selectorNode.Address.Offset:X4}, [{signatureBytes}])";
    }

    // ----------------------------------------------------------------------------------------------------
    // Conditional control flow. VisitIfElseNode owns the one irreducible orchestration: a MoveIpNextNode
    // fallthrough arm must exclude the *other* arm's target, context a single arm cannot see, so the two arms
    // are composed here rather than each lowering itself in isolation.
    // ----------------------------------------------------------------------------------------------------

    public EmittedCode VisitIfElseNode(IfElseNode node) {
        if (!NodeContainsControlFlow(node)) {
            // Pure data conditional (ternary-like): both arms are always emitted, including an empty `else`
            // block, matching the historical statement emitter.
            return EmittedCode.Statements(
                new BlockStatement($"if ({Expr(node.Condition)})", node.TrueCase.Accept(this).AsStatements()),
                new BlockStatement("else", node.FalseCase.Accept(this).AsStatements()));
        }

        // Control-flow conditional: at least one arm transfers, so it carries the source instruction.
        CfgInstruction instruction = FindArmInstruction(node)
            ?? throw new NotSupportedException("A control-flow conditional has no instruction-bearing arm to resolve typed CFG edges against.");
        string condition = Expr(node.Condition);

        EmittedCode trueArm = LowerConditionalArm(instruction, node.TrueCase);
        EmittedCode falseArm = LowerConditionalArm(instruction, node.FalseCase);

        if (!trueArm.IsEmpty) {
            List<StatementItem> items = [new BlockStatement($"if ({condition})", trueArm.AsStatements())];
            if (!falseArm.IsEmpty) {
                items.Add(new BlockStatement("else", falseArm.AsStatements()));
            }
            return EmittedCode.Statements(items);
        }

        // The taken arm is empty (e.g. a fallthrough that is the next emitted node). Emit only the
        // remaining arm under the negated condition rather than an empty `if` with a populated `else`.
        if (!falseArm.IsEmpty) {
            return EmittedCode.Statements(new BlockStatement($"if (!({condition}))", falseArm.AsStatements()));
        }

        return EmittedCode.None;
    }

    private static CfgInstruction? FindArmInstruction(IfElseNode node) =>
        FindArmInstruction(node.TrueCase) ?? FindArmInstruction(node.FalseCase);

    private static CfgInstruction? FindArmInstruction(IVisitableAstNode arm) => arm switch {
        CfgInstructionNode instructionNode => instructionNode.Instruction,
        BlockNode blockNode => blockNode.Statements.Select(FindArmInstruction).FirstOrDefault(found => found is not null),
        _ => null
    };

    /// <summary>
    /// Lowers one arm of a control-flow conditional. Most arms lower exactly like their standalone form, so
    /// they go through the AST's own <c>Accept</c> dispatch. Only the arms that genuinely depend on the
    /// conditional context are special-cased.
    /// </summary>
    private EmittedCode LowerConditionalArm(CfgInstruction instruction, IVisitableAstNode arm) => arm switch {
        JumpNearNode jumpNearNode => LowerConditionalNearJumpArm(jumpNearNode),
        MoveIpNextNode => LowerFallthroughArm(instruction),
        BlockNode blockNode => EmittedCode.Statements(blockNode.Statements
            .SelectMany(statement => LowerConditionalArm(instruction, statement).AsStatements())
            .ToList()),
        _ => arm.Accept(this)
    };

    private EmittedCode LowerConditionalNearJumpArm(JumpNearNode node) {
        if (TryGetConstantWord(node.Ip) is ushort offset) {
            // Constant target: transfer to the observed edge when discovery traversed it. When the taken
            // branch was never observed but its target block was discovered in this same partition, synthesize
            // the edge and emit a normal goto rather than failing: only the edge is untested, not the target.
            if (TryResolveNearJump(node) is ResolvedCfgEdge edge) {
                return Transfer.Emit(edge, CurrentMethod);
            }
            SegmentedAddress targetAddress = new(node.Instruction.Address.Segment, offset);
            if (Context.TryResolveSamePartitionBlockEntry(node.Instruction, targetAddress) is ResolvedCfgEdge blockEntryEdge) {
                return Transfer.Emit(blockEntryEdge, CurrentMethod);
            }
            return EmittedCode.Diverging($"throw FailAsUntested(\"Unobserved conditional jump target at {node.Instruction.Address}\");");
        }
        return BuildNearRuntimeDispatch(node.Instruction, Expr(node.Ip), "jump",
            edge => Transfer.Emit(edge, CurrentMethod).AsStatements());
    }

    /// <summary>
    /// Lowers a <see cref="MoveIpNextNode"/> fallthrough arm. The fallthrough target is, by construction, the
    /// instruction that statically follows the conditional in memory, so it is resolved directly against that
    /// address. When that edge was never observed during discovery (the conditional was always taken) but the
    /// following block was discovered in this same partition, the edge is synthesized and lowered as a normal
    /// goto; only when no such block exists does the generated code fail as untested.
    /// </summary>
    private EmittedCode LowerFallthroughArm(CfgInstruction instruction) {
        SegmentedAddress fallthroughAddress = instruction.NextInMemoryAddress32.ToSegmentedAddress();
        if (TryResolveConstantTarget(instruction, fallthroughAddress) is ResolvedCfgEdge edge) {
            return Transfer.Emit(edge, CurrentMethod, allowCallOutContinuation: true);
        }
        if (Context.TryResolveSamePartitionBlockEntry(instruction, fallthroughAddress) is ResolvedCfgEdge blockEntryEdge) {
            return Transfer.Emit(blockEntryEdge, CurrentMethod);
        }
        return EmittedCode.Diverging($"throw FailAsUntested(\"Unobserved conditional fallthrough at {instruction.Address}\");");
    }

    private ResolvedCfgEdge? TryResolveNearJump(JumpNearNode node) {
        if (TryGetConstantWord(node.Ip) is not ushort offset) {
            return null;
        }

        return TryResolveConstantTarget(node.Instruction, new SegmentedAddress(node.Instruction.Address.Segment, offset));
    }

    /// <summary>
    /// Resolves the single observed normal successor of <paramref name="instruction"/> whose target is
    /// <paramref name="targetAddress"/>; <c>null</c> when none was observed, throwing when the target is
    /// observed more than once (ambiguous semantic dispatch).
    /// </summary>
    private ResolvedCfgEdge? TryResolveConstantTarget(CfgInstruction instruction, SegmentedAddress targetAddress) =>
        Context.TryResolveEdge(instruction, InstructionSuccessorType.Normal, targetAddress);

    // ----------------------------------------------------------------------------------------------------
    // Runtime dispatch builders shared by jump and call lowering. The per-edge body differs (a plain
    // transfer for jumps, a call helper plus continuation for calls), so it is supplied by the caller.
    // ----------------------------------------------------------------------------------------------------

    private EmittedCode BuildNearRuntimeDispatch(CfgInstruction instruction, string targetExpression, string targetKind,
        Func<ResolvedCfgEdge, IReadOnlyList<StatementItem>> bodyForEdge) {
        IReadOnlyList<ResolvedCfgEdge> edges = RequireObservedEdges(instruction, targetKind);
        List<SwitchCase> cases = edges
            .OrderBy(edge => edge.Target.Address.Offset)
            .Select(edge => new SwitchCase($"0x{edge.Target.Address.Offset:X4}", bodyForEdge(edge)))
            .ToList();
        List<StatementItem> defaultBody = [new LineStatement($"throw FailAsUntested($\"Unknown near {targetKind} target 0x{{((ushort)({targetExpression})):X4}} at {instruction.Address}\");", Diverges: true)];
        return EmittedCode.Statements(new SwitchStatement($"switch ((ushort)({targetExpression}))", cases, defaultBody));
    }

    private EmittedCode BuildFarRuntimeDispatch(CfgInstruction instruction, string segmentExpression, string offsetExpression, string targetKind,
        Func<ResolvedCfgEdge, IReadOnlyList<StatementItem>> bodyForEdge) {
        IReadOnlyList<ResolvedCfgEdge> edges = RequireObservedEdges(instruction, $"far {targetKind}");
        string segmentVariable = $"targetSegment_{instruction.Id}";
        string offsetVariable = $"targetOffset_{instruction.Id}";
        List<StatementItem> items = [
            new LineStatement($"ushort {segmentVariable} = unchecked((ushort)({segmentExpression}));"),
            new LineStatement($"ushort {offsetVariable} = unchecked((ushort)({offsetExpression}));")
        ];
        foreach (ResolvedCfgEdge edge in edges.OrderBy(edge => edge.Target.Address.Segment).ThenBy(edge => edge.Target.Address.Offset)) {
            // The matched branch must transfer explicitly (the body uses forceSameMethodGoto) so it never
            // falls through to the trailing untested-target failure even when the target is the next node.
            items.Add(new BlockStatement(
                $"if ({segmentVariable} == {Context.GetSegmentVariable(edge.Target.Address.Segment)} && {offsetVariable} == 0x{edge.Target.Address.Offset:X4})",
                bodyForEdge(edge)));
        }
        items.Add(new LineStatement($"throw FailAsUntested($\"Unknown far {targetKind} target {{{segmentVariable}:X4}}:{{{offsetVariable}:X4}} at {instruction.Address}\");", Diverges: true));
        return EmittedCode.Statements(items);
    }

    private IReadOnlyList<ResolvedCfgEdge> RequireObservedEdges(CfgInstruction instruction, string targetKind) {
        IReadOnlyList<ResolvedCfgEdge> edges = Context.GetObservedEdges(instruction, InstructionSuccessorType.Normal);
        if (edges.Count == 0) {
            throw new NotSupportedException($"Instruction {instruction.Address} has no observed normal {targetKind} targets.");
        }
        return edges;
    }

    private IReadOnlyList<StatementItem> CallBranchBody(CfgInstruction instruction, CallContinuation continuation,
        string helperName, ResolvedCfgEdge edge, bool isFarCall, bool forceSameMethodGoto) {
        ushort? targetCs = isFarCall ? edge.Target.Address.Segment : null;
        string callLine = Transfer.BuildCallHelperLine(helperName, continuation.ExpectedReturnAddress, Transfer.FunctionExpression(edge), targetCs);
        return EmittedCode.Concat(
            EmittedCode.Line(callLine),
            Transfer.EmitPostCallContinuation(instruction, continuation, CurrentMethod, forceSameMethodGoto: forceSameMethodGoto)).AsStatements();
    }

    private static ushort? TryGetConstantWord(IVisitableAstNode node) => node switch {
        ConstantNode constantNode => unchecked((ushort)constantNode.Value),
        _ => null
    };

    // ----------------------------------------------------------------------------------------------------
    // Expression and simple-statement visit methods. These need no CFG context: they lower a node directly
    // from its own syntax.
    // ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lowers <paramref name="node"/> to the expression arm. Throws if the node lowered to statements, which
    /// is a generator bug (an expression was expected in the position this child sits in).
    /// </summary>
    private CSharpFragment Expr(IVisitableAstNode node) => node.Accept(this).AsExpression();

    private string LocalVariableName(string variableName) =>
        _localVariableSuffix.Length == 0 ? variableName : $"{variableName}_{_localVariableSuffix}";

    public EmittedCode VisitBlockNode(BlockNode node) =>
        EmittedCode.Statements(node.Statements.SelectMany(statement => statement.Accept(this).AsStatements()).ToList());

    public EmittedCode VisitMoveIpNextNode(MoveIpNextNode node) => EmittedCode.None;

    public EmittedCode VisitWhileNode(WhileNode node) =>
        EmittedCode.Statements(new BlockStatement($"while ({Expr(node.Condition)})", node.Body.Accept(this).AsStatements()));

    public EmittedCode VisitHltNode(HltNode node) => EmittedCode.Diverging("return Hlt();");

    public EmittedCode VisitInvalidInstructionNode(InvalidInstructionNode node) =>
        EmittedCode.Diverging($"throw new {node.CpuException.GetType().FullName}(\"{EscapeString(node.CpuException.Message)}\");");

    public EmittedCode VisitCpuidNode(CpuidNode node) =>
        EmittedCode.Diverging("throw new CpuInvalidOpcodeException(\"Attempted to call CPUID, which is unsupported on CPUs < 486\");");

    public EmittedCode VisitThrowNode(ThrowNode node) =>
        EmittedCode.Diverging($"throw new {node.ExceptionType.FullName}(\"{EscapeString(node.Message)}\");");

    public EmittedCode VisitReturnNearNode(ReturnNearNode node) => LowerReturn(NearRetExpression(node));
    public EmittedCode VisitReturnFarNode(ReturnFarNode node) => LowerReturn(FarRetExpression(node));
    public EmittedCode VisitReturnInterruptNode(ReturnInterruptNode node) => LowerReturn("InterruptRet()");

    private static EmittedCode LowerReturn(string returnActionExpression) =>
        EmittedCode.Diverging($"return {returnActionExpression};");

    public EmittedCode VisitMethodCallNode(MethodCallNode node) {
        if (TryEmitIoPortCall(node, out string ioPortCall)) {
            return (CSharpFragment)ioPortCall;
        }

        string target = node.PropertyPath is null ? string.Empty : node.PropertyPath + ".";
        string arguments = string.Join(", ", FormatMethodArguments(node));
        return (CSharpFragment)$"{target}{node.MethodName}({arguments})";
    }

    /// <summary>
    /// IN/OUT execution AST nodes are emitted as root-helper calls (In8/Out16/...) by the IO AST builder.
    /// Those helpers do not exist on <see cref="CSharpOverrideHelper"/>, so generated overrides reach the I/O
    /// bus through <c>Machine.IoPortDispatcher</c> instead.
    /// </summary>
    private bool TryEmitIoPortCall(MethodCallNode node, out string result) {
        if (node.PropertyPath is not null) {
            result = string.Empty;
            return false;
        }

        (string dispatcherMethod, DataType? valueType) = node.MethodName switch {
            "In8" => ("ReadByte", null),
            "In16" => ("ReadWord", null),
            "In32" => ("ReadDWord", null),
            "Out8" => ("WriteByte", DataType.UINT8),
            "Out16" => ("WriteWord", DataType.UINT16),
            "Out32" => ("WriteDWord", DataType.UINT32),
            _ => (string.Empty, null)
        };
        if (dispatcherMethod.Length == 0) {
            result = string.Empty;
            return false;
        }

        string port = Cast(DataType.UINT16, Expr(node.Arguments[0]));
        if (valueType is null) {
            result = $"Machine.IoPortDispatcher.{dispatcherMethod}({port})";
            return true;
        }

        string value = Cast(valueType, Expr(node.Arguments[1]));
        result = $"Machine.IoPortDispatcher.{dispatcherMethod}({port}, {value})";
        return true;
    }

    private IEnumerable<string> FormatMethodArguments(MethodCallNode node) {
        Type? targetType = ResolveKnownMethodTargetType(node.PropertyPath);
        if (targetType is null) {
            return node.Arguments.Select(argument => Expr(argument).Text);
        }

        List<MethodInfo> candidates = targetType.GetMethods()
            .Where(method => method.Name == node.MethodName && method.GetParameters().Length == node.Arguments.Count)
            .ToList();
        if (candidates.Count == 0) {
            return node.Arguments.Select(argument => Expr(argument).Text);
        }
        if (candidates.Count > 1) {
            throw new NotSupportedException(
                $"{targetType.Name}.{node.MethodName} has {candidates.Count} overloads taking {node.Arguments.Count} arguments; the generator cannot pick parameter types for argument casts.");
        }

        ParameterInfo[] parameters = candidates[0].GetParameters();
        return node.Arguments.Select((argument, index) => Cast(parameters[index].ParameterType, Expr(argument)));
    }

    private static Type? ResolveKnownMethodTargetType(string? propertyPath) => propertyPath switch {
        nameof(Alu8) => typeof(Alu8),
        nameof(Alu16) => typeof(Alu16),
        nameof(Alu32) => typeof(Alu32),
        nameof(Stack) => typeof(Stack),
        _ => null
    };

    public EmittedCode VisitMethodCallValueNode(MethodCallValueNode node) {
        MethodCallNode callNode = node.CallNode;
        if (callNode.PropertyPath == nameof(State) && callNode.Arguments.Count == 0) {
            return (CSharpFragment)$"{callNode.PropertyPath}.{callNode.MethodName}";
        }

        return Atomic(Expr(callNode), node.DataType);
    }

    public EmittedCode VisitRegisterNode(RegisterNode node) =>
        Atomic(_registerRenderer.ToStringRegister(node.DataType.BitWidth, node.RegisterIndex), node.DataType);

    public EmittedCode VisitSegmentRegisterNode(SegmentRegisterNode node) =>
        Atomic(_registerRenderer.ToStringSegmentRegister(node.RegisterIndex), DataType.UINT16);

    public EmittedCode VisitSegmentedPointer(SegmentedPointerNode node) {
        string indexer = ToMemoryIndexer(node.DataType);
        string segment = Expr(node.Segment);
        CSharpFragment offset = Expr(node.Offset);
        return Atomic($"{indexer}[{segment}, {MemoryOffset(offset)}]", node.DataType);
    }

    public EmittedCode VisitAbsolutePointerNode(AbsolutePointerNode node) =>
        Atomic($"{ToMemoryIndexer(node.DataType)}[unchecked((uint)({Expr(node.AbsoluteAddress)}))]", node.DataType);

    public EmittedCode VisitConstantNode(ConstantNode node) {
        if (node.DataType == DataType.BOOL) {
            return Atomic(node.Value == 0 ? "false" : "true", DataType.BOOL);
        }

        string literal = node.DataType.BitWidth switch {
            BitWidth.BYTE_8 => node.DataType.Signed ? $"(sbyte){node.SignedValue}" : $"(byte)0x{node.Value:X2}",
            BitWidth.WORD_16 => node.DataType.Signed ? $"(short){node.SignedValue}" : $"(ushort)0x{node.Value:X4}",
            BitWidth.DWORD_32 or BitWidth.BOOL_1 => node.DataType.Signed ? $"{node.SignedValue}" : $"0x{node.Value:X8}u",
            BitWidth.QWORD_64 => node.DataType.Signed ? $"{node.SignedValue}L" : $"0x{node.Value:X16}UL",
            _ => throw Unsupported(node)
        };
        return Atomic(literal, node.DataType);
    }

    public EmittedCode VisitNearAddressNode(NearAddressNode node) => VisitConstantNode(node);

    public EmittedCode VisitBinaryOperationNode(BinaryOperationNode node) {
        if (node.BinaryOperation == BinaryOperation.ASSIGN) {
            // No outer parentheses: as a statement the consumer appends `;` (`x = y;`); as a sub-expression
            // the low assignment precedence makes Parenthesize wrap it (`(x = y)`).
            string assignment = $"{Expr(node.Left)} = {EmitAssignmentRight(node)}";
            return new CSharpFragment(assignment, node.DataType, AssignmentPrecedence);
        }

        (string op, int precedence) = node.BinaryOperation switch {
            BinaryOperation.MULTIPLY => ("*", MultiplicativePrecedence),
            BinaryOperation.DIVIDE => ("/", MultiplicativePrecedence),
            BinaryOperation.MODULO => ("%", MultiplicativePrecedence),
            BinaryOperation.PLUS => ("+", AdditivePrecedence),
            BinaryOperation.MINUS => ("-", AdditivePrecedence),
            BinaryOperation.LEFT_SHIFT => ("<<", ShiftPrecedence),
            BinaryOperation.RIGHT_SHIFT => (">>", ShiftPrecedence),
            BinaryOperation.LESS_THAN => ("<", RelationalPrecedence),
            BinaryOperation.GREATER_THAN => (">", RelationalPrecedence),
            BinaryOperation.LESS_THAN_OR_EQUAL => ("<=", RelationalPrecedence),
            BinaryOperation.GREATER_THAN_OR_EQUAL => (">=", RelationalPrecedence),
            BinaryOperation.EQUAL => ("==", EqualityPrecedence),
            BinaryOperation.NOT_EQUAL => ("!=", EqualityPrecedence),
            BinaryOperation.BITWISE_AND => ("&", BitwiseAndPrecedence),
            BinaryOperation.BITWISE_XOR => ("^", BitwiseXorPrecedence),
            BinaryOperation.BITWISE_OR => ("|", BitwiseOrPrecedence),
            BinaryOperation.LOGICAL_AND => ("&&", LogicalAndPrecedence),
            BinaryOperation.LOGICAL_OR => ("||", LogicalOrPrecedence),
            _ => throw Unsupported(node)
        };

        // Left-associative: the left operand may share the operator's precedence without parentheses; the
        // right operand needs them when it binds equally or more loosely.
        string left = Parenthesize(Expr(node.Left), precedence);
        string right = Parenthesize(Expr(node.Right), precedence + 1);
        // C# promotes byte/ushort/short/sbyte arithmetic to int, so the actual evaluated type is not the
        // node's semantic DataType; leave it unknown so a cast back to the operand width is never elided.
        DataType? resultType = node.DataType == DataType.BOOL ? DataType.BOOL : null;
        return new CSharpFragment($"{left} {op} {right}", resultType, precedence);
    }

    public EmittedCode VisitUnaryOperationNode(UnaryOperationNode node) {
        string op = node.UnaryOperation switch {
            UnaryOperation.NOT => "!",
            UnaryOperation.NEGATE => "-",
            UnaryOperation.BITWISE_NOT => "~",
            _ => throw Unsupported(node)
        };
        string operand = Parenthesize(Expr(node.Value), UnaryPrecedence);
        // Logical NOT yields bool; arithmetic/bitwise unary ops promote to int, so their evaluated type is
        // not the node's semantic DataType.
        DataType? resultType = node.UnaryOperation == UnaryOperation.NOT ? DataType.BOOL : null;
        return new CSharpFragment($"{op}{operand}", resultType, UnaryPrecedence);
    }

    public EmittedCode VisitTypeConversionNode(TypeConversionNode node) {
        CSharpFragment inner = Expr(node.Value);
        if (inner.Type == node.DataType || node.DataType == DataType.BOOL && inner.Type == DataType.BOOL) {
            // Converting to a type the value already has is a no-op; keep the inner fragment as-is.
            return inner;
        }
        string text = Cast(node.DataType, inner);
        return new CSharpFragment(text, node.DataType, CSharpFragment.AtomicPrecedence);
    }

    public EmittedCode VisitFlagRegisterNode(FlagRegisterNode node) =>
        node.DataType.BitWidth == BitWidth.WORD_16
            ? Atomic("FlagRegister16", DataType.UINT16)
            : Atomic("FlagRegister", DataType.UINT32);

    public EmittedCode VisitCpuFlagNode(CpuFlagNode node) => Atomic(node.FlagMask switch {
        Flags.Carry => "CarryFlag",
        Flags.Parity => "ParityFlag",
        Flags.Auxiliary => "AuxiliaryFlag",
        Flags.Zero => "ZeroFlag",
        Flags.Sign => "SignFlag",
        Flags.Trap => "TrapFlag",
        Flags.Interrupt => "InterruptFlag",
        Flags.Direction => "DirectionFlag",
        Flags.Overflow => "OverflowFlag",
        _ => throw Unsupported(node)
    }, DataType.BOOL);

    public EmittedCode VisitInstructionFieldNode(InstructionFieldNode node) => node.ResolvedNode.Accept(this);
    public EmittedCode VisitVariableReferenceNode(VariableReferenceNode node) => (CSharpFragment)LocalVariableName(node.VariableName);
    public EmittedCode VisitVariableDeclarationNode(VariableDeclarationNode node) =>
        (CSharpFragment)$"{ToCSharpType(node.DataType)} {LocalVariableName(node.VariableName)} = {Cast(node.DataType, Expr(node.Initializer))}";
    public EmittedCode VisitInstructionNode(InstructionNode node) => throw Unsupported(node);
    public EmittedCode VisitSegmentedAddressNode(SegmentedAddressNode node) =>
        (CSharpFragment)$"new SegmentedAddress(unchecked((ushort)({Expr(node.Segment)})), unchecked((ushort)({Expr(node.Offset)})))";

    private string NearRetExpression(ReturnNearNode node) {
        string helperName = node.RetBitWidth == BitWidth.WORD_16 ? "NearRet" : "NearRet32";
        return $"{helperName}({Expr(node.BytesToPop)})";
    }

    private string FarRetExpression(ReturnFarNode node) {
        string helperName = node.RetBitWidth == BitWidth.WORD_16 ? "FarRet" : "FarRet32";
        return $"{helperName}({Expr(node.BytesToPop)})";
    }

    private string EmitAssignmentRight(BinaryOperationNode assignment) {
        CSharpFragment expression = Expr(assignment.Right);
        return Cast(assignment.Left.DataType, expression);
    }

    private static string ToMemoryIndexer(DataType dataType) => dataType.BitWidth switch {
        BitWidth.BYTE_8 => dataType.Signed ? "Int8" : "UInt8",
        BitWidth.WORD_16 => dataType.Signed ? "Int16" : "UInt16",
        BitWidth.DWORD_32 or BitWidth.BOOL_1 => dataType.Signed ? "Int32" : "UInt32",
        _ => throw new NotSupportedException($"Unsupported memory data type {dataType}.")
    };

    private static string ToCSharpType(DataType dataType) {
        if (dataType == DataType.BOOL) {
            return "bool";
        }

        return dataType.BitWidth switch {
            BitWidth.BYTE_8 => dataType.Signed ? "sbyte" : "byte",
            BitWidth.WORD_16 => dataType.Signed ? "short" : "ushort",
            BitWidth.DWORD_32 or BitWidth.BOOL_1 => dataType.Signed ? "int" : "uint",
            BitWidth.QWORD_64 => dataType.Signed ? "long" : "ulong",
            _ => throw new NotSupportedException($"Unsupported data type {dataType}.")
        };
    }

    private static string Cast(DataType dataType, CSharpFragment expression) {
        if (dataType == DataType.BOOL) {
            // A bool expression never needs a cast; a non-bool would not be assignable to bool anyway.
            return expression.Text;
        }
        if (expression.Type == dataType) {
            // Already the target type: no cast needed.
            return expression.Text;
        }

        string targetType = ToCSharpType(dataType);
        string inner = Parenthesize(expression, CastPrecedence);
        // Always wrap in unchecked so a narrowing cast truncates (matching x86 wraparound) regardless of
        // whether the consuming project compiles in a checked overflow context. This applies to signed and
        // unsigned targets alike.
        return $"unchecked(({targetType}){inner})";
    }

    private static string Cast(Type targetType, CSharpFragment expression) {
        if (targetType == typeof(bool)) {
            return expression.Text;
        }

        DataType? csharpType = TryToDataType(targetType);
        if (csharpType is not null) {
            return Cast(csharpType, expression);
        }

        return expression.Text;
    }

    private static CSharpFragment Atomic(string text, DataType type) => new(text, type, CSharpFragment.AtomicPrecedence);

    /// <summary>
    /// Renders a memory-indexer offset. A constant or single register/variable offset is emitted directly
    /// (the indexer has a <c>ushort</c> overload); a compound arithmetic offset is wrapped in
    /// <c>(ushort)(...)</c> so wrap-around matches real-mode 16-bit offset arithmetic.
    /// </summary>
    private static string MemoryOffset(CSharpFragment offset) {
        if (offset.Precedence >= CSharpFragment.AtomicPrecedence) {
            return offset.Text;
        }
        return $"(ushort)({offset.Text})";
    }

    /// <summary>
    /// Wraps <paramref name="fragment"/> in parentheses only when its top-level operator binds more loosely
    /// than <paramref name="minimumPrecedence"/> requires.
    /// </summary>
    private static string Parenthesize(CSharpFragment fragment, int minimumPrecedence) {
        if (fragment.Precedence >= minimumPrecedence) {
            return fragment.Text;
        }
        return $"({fragment.Text})";
    }

    private static string ToCSharpType(Type type) =>
        ClrTypesByType.TryGetValue(type, out ClrTypeInfo info) ? info.Name : type.FullName ?? type.Name;

    private static DataType? TryToDataType(Type type) =>
        ClrTypesByType.TryGetValue(type, out ClrTypeInfo info) ? info.DataType : null;

    private static NotSupportedException Unsupported(IVisitableAstNode node) =>
        new($"CFG C# generation does not support AST node {node.GetType().FullName} yet.");

    private static string EscapeString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private readonly record struct ClrTypeInfo(string Name, DataType? DataType);
}
