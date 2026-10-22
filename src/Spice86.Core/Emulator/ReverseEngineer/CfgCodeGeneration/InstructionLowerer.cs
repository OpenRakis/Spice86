namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

using CfgSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

/// <summary>
/// Lowers an instruction AST to a tree of runtime and code-generation nodes. Control flow becomes concrete
/// <c>goto</c>s, helper calls, and dispatch switches using the CFG generation context; expression nodes are
/// returned unchanged because they are immutable. The transfer mechanics themselves are delegated to
/// <see cref="TransferEmitter"/>. Printing the result is <see cref="CSharpStatementPrinter"/>'s job.
/// </summary>
internal sealed class InstructionLowerer : IAstVisitor<IVisitableAstNode> {
    private MethodPlan? _currentMethod;

    /// <summary>
    /// Constructs a lowerer wired with the CFG-aware services it consults when lowering control-flow nodes:
    /// the generation context and the transfer-lowering service.
    /// </summary>
    public InstructionLowerer(CfgGeneratorContext context, TransferEmitter transferEmitter) {
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

    // ----------------------------------------------------------------------------------------------------
    // Per-instruction driving entry. The lowering itself is uniform Accept dispatch; this method only adds
    // the instruction-level fallthrough transfer (the one inter-instruction concern the AST node cannot see).
    // ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lowers the body of an instruction whose execution AST is rooted at <paramref name="node"/> through the
    /// AST's own <c>Accept</c> dispatch, then appends the fallthrough transfer
    /// (<see cref="TransferEmitter.EmitFallthroughIfNeeded"/>) unless the body already terminates control flow.
    /// </summary>
    public BlockNode LowerInstructionBody(CfgInstruction instruction, IVisitableAstNode node, bool emitExternalEventCheckAfter) {
        BlockNode body = DivisionPattern.TryMatch(node, instruction.Address) is DivisionPattern division
            ? Sequence(new DivisionNode(division))
            : Sequence(node.Accept(this));
        // Skip the fallthrough transfer when the node owns its control flow, or when the lowered body already
        // diverges (e.g. a CPUID/throw node lowering to a `throw`): appending a fallthrough after a diverging
        // body would be unreachable code (CS0162).
        if (TerminatesControlFlow(node) || !CompletesNormallyVisitor.CompletesNormally(body)) {
            return body;
        }
        IVisitableAstNode eventCheck = Empty();
        if (emitExternalEventCheckAfter) {
            eventCheck = new CheckExternalEventsNode(new SegmentedAddress(instruction.Address.Segment, (ushort)instruction.NextInMemoryAddress32.Offset));
        }
        return Sequence(body, eventCheck, Transfer.EmitFallthroughIfNeeded(instruction, CurrentMethod));
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

    public IVisitableAstNode VisitJumpNearNode(JumpNearNode node) {
        if (TryGetConstantWord(node.Ip) is ushort offset) {
            return Transfer.Emit(Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
                new SegmentedAddress(node.Instruction.Address.Segment, offset)), CurrentMethod);
        }

        return BuildNearRuntimeDispatch(node.Instruction, node.Ip, "jump",
            edge => Transfer.Emit(edge, CurrentMethod));
    }

    public IVisitableAstNode VisitJumpFarNode(JumpFarNode node) {
        ushort? segment = TryGetConstantWord(node.TargetAddress.Segment);
        ushort? offset = TryGetConstantWord(node.TargetAddress.Offset);
        if (segment is not null && offset is not null) {
            return Transfer.Emit(Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
                new SegmentedAddress(segment.Value, offset.Value)), CurrentMethod);
        }

        return BuildFarRuntimeDispatch(node.Instruction, node.TargetAddress.Segment, node.TargetAddress.Offset, "jump",
            // forceSameMethodGoto: far dispatch is a flat chain of sibling `if`s with the untested-target
            // throw as the next sibling (unlike the near switch, whose `break` exits past the construct to
            // the next node). An empty matched branch would fall through the remaining checks into that
            // throw, so the adjacency-fallthrough optimization cannot apply here.
            edge => Transfer.Emit(edge, CurrentMethod, forceSameMethodGoto: true));
    }

    public IVisitableAstNode VisitCallNearNode(CallNearNode node) {
        string helperName = node.CallBitWidth == BitWidth.WORD_16 ? "NearCall" : "NearCall32";
        if (TryGetConstantWord(node.TargetIp) is not ushort targetOffset) {
            CallContinuation runtimeContinuation = Context.ResolveCallContinuation(node.Instruction);
            return BuildNearRuntimeDispatch(node.Instruction, node.TargetIp, "call",
                edge => CallBranchBody(node.Instruction, runtimeContinuation, helperName, edge, isFarCall: false, forceSameMethodGoto: false));
        }

        ResolvedCfgEdge targetEdge = Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
            new SegmentedAddress(node.Instruction.Address.Segment, targetOffset));
        return Transfer.EmitCallHelperAndContinuation(helperName, node.Instruction, Transfer.FunctionReference(targetEdge), CurrentMethod);
    }

    public IVisitableAstNode VisitCallFarNode(CallFarNode node) {
        string helperName = node.CallBitWidth == BitWidth.WORD_16 ? "FarCall" : "FarCall32";
        ushort? targetSegment = TryGetConstantWord(node.TargetAddress.Segment);
        ushort? targetOffset = TryGetConstantWord(node.TargetAddress.Offset);
        if (targetSegment is null || targetOffset is null) {
            CallContinuation runtimeContinuation = Context.ResolveCallContinuation(node.Instruction);
            return BuildFarRuntimeDispatch(node.Instruction, node.TargetAddress.Segment, node.TargetAddress.Offset, "call",
                edge => CallBranchBody(node.Instruction, runtimeContinuation, helperName, edge, isFarCall: true, forceSameMethodGoto: true));
        }

        SegmentedAddress targetAddress = new SegmentedAddress(targetSegment.Value, targetOffset.Value);
        ResolvedCfgEdge targetEdge = Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal, targetAddress);
        return Transfer.EmitCallHelperAndContinuation(helperName, node.Instruction, Transfer.FunctionReference(targetEdge), CurrentMethod, farCallTargetCs: targetSegment.Value);
    }

    public IVisitableAstNode VisitInterruptCallNode(InterruptCallNode node) {
        CallContinuation continuation = Context.ResolveCallContinuation(node.Instruction);
        SegmentedAddress expectedReturn = continuation.ExpectedReturnAddress;
        return Sequence(
            new MethodCallNode(null, "InterruptCall",
                new SegmentVariableNode(expectedReturn.Segment),
                new OffsetLiteralNode(expectedReturn.Offset),
                new ExplicitCastNode(DataType.UINT8, node.VectorNumber)),
            Transfer.EmitPostCallContinuation(node.Instruction, continuation, CurrentMethod));
    }

    public IVisitableAstNode VisitCallbackNode(CallbackNode node) {
        IReadOnlyList<ResolvedCfgEdge> movedTargets = Context.GetSuccessorEdges(node.Instruction, InstructionSuccessorType.Normal)
            .Where(edge => edge.Target.Address != node.Instruction.NextInMemoryAddress32.ToSegmentedAddress())
            .ToList();
        IVisitableAstNode callback = movedTargets.Count == 0
            ? EmitDirectCallback(node)
            : BuildCallbackTargetDispatch(node, movedTargets);
        return Sequence(
            callback,
            Transfer.EmitFallthroughIfNeeded(node.Instruction, CurrentMethod));
    }

    /// <summary>
    /// Lowers a callback whose handler kept CS:IP at the callback during discovery: a direct
    /// <c>Callback</c> call that returns into the emitted fallthrough.
    /// </summary>
    private static MethodCallNode EmitDirectCallback(CallbackNode node) =>
        new(null, "Callback",
            new SegmentVariableNode(node.Instruction.Address.Segment),
            new OffsetLiteralNode(node.Instruction.Address.Offset),
            new ExplicitCastNode(DataType.UINT16, node.CallbackNumber));

    /// <summary>
    /// Lowers a callback whose handler moved CS:IP during discovery (DOS EXEC, terminate, TSR): run the
    /// callback, and when it moved CS:IP again dispatch over the observed targets, failing as untested on a
    /// target discovery never saw. The instruction's own fallthrough is still emitted after this block; it is
    /// reachable only when the handler kept CS:IP at the callback.
    /// </summary>
    private IVisitableAstNode BuildCallbackTargetDispatch(CallbackNode node, IReadOnlyList<ResolvedCfgEdge> movedTargets) {
        List<IVisitableAstNode> targets = [];
        foreach (ResolvedCfgEdge edge in movedTargets
                     .OrderBy(edge => edge.Target.Address.Segment)
                     .ThenBy(edge => edge.Target.Address.Offset)) {
            targets.Add(new IfElseNode(
                AddressEquals(new SegmentRegisterNode((int)SegmentRegisterIndex.CsIndex), new InstructionPointerNode(), edge.Target.Address),
                Sequence(Transfer.Emit(edge, CurrentMethod, forceSameMethodGoto: true)),
                Empty()));
        }
        targets.Add(UntestedMessages.CallbackTarget(node.Instruction.Address));
        MethodCallValueNode movedCsIp = new(DataType.BOOL, null, "CallbackMovedCsIp",
            new SegmentVariableNode(node.Instruction.Address.Segment),
            new OffsetLiteralNode(node.Instruction.Address.Offset),
            new ExplicitCastNode(DataType.UINT16, node.CallbackNumber));
        return new IfElseNode(movedCsIp, Sequence(targets), Empty());
    }

    public IVisitableAstNode VisitSelectorNode(SelectorNode node) => LowerSelector(node.CfgSelector);

    /// <summary>
    /// Lowers a self-modifying-code selector. Branches are ordered to match the interpreter oracle
    /// (<see cref="CfgSelectorNode.GetNextSuccessor"/>), which iterates signatures in
    /// <see cref="Signature"/> order (ascending length first, see <see cref="Signature.CompareTo"/>) and
    /// takes the <em>first</em> equivalent match. Because a <c>null</c> signature byte is a wildcard, two
    /// signatures of different lengths can both match the same memory, so the generated branch order must be
    /// the same ascending order to pick the same variant as the interpreter. If no signature matches the
    /// generated code fails as untested.
    /// </summary>
    private IVisitableAstNode LowerSelector(CfgSelectorNode selectorNode) {
        List<IVisitableAstNode> items = [];
        foreach (CfgInstruction target in selectorNode.SuccessorsPerSignature
                     .OrderBy(entry => entry.Key)
                     .Select(entry => entry.Value)) {
            ResolvedCfgEdge edge = new(selectorNode, target, InstructionSuccessorType.Normal,
                Context.FindTransfer(selectorNode, target, InstructionSuccessorType.Normal)?.Kind);
            MethodCallValueNode signatureMatches = new(DataType.BOOL, null, "SelectorSignatureMatches",
                new SegmentVariableNode(selectorNode.Address.Segment),
                new OffsetLiteralNode(selectorNode.Address.Offset),
                new SignatureFieldNode(Context.GetSignatureField(target)));
            items.Add(new IfElseNode(signatureMatches,
                Sequence(Transfer.Emit(edge, CurrentMethod, forceSameMethodGoto: true)),
                Empty()));
        }
        items.Add(UntestedMessages.CodeVariant(selectorNode.Address));
        return Sequence(items);
    }

    // ----------------------------------------------------------------------------------------------------
    // Conditional control flow. VisitIfElseNode owns the one irreducible orchestration: a MoveIpNextNode
    // fallthrough arm must exclude the *other* arm's target, context a single arm cannot see, so the two arms
    // are composed here rather than each lowering itself in isolation.
    // ----------------------------------------------------------------------------------------------------

    public IVisitableAstNode VisitIfElseNode(IfElseNode node) {
        if (!NodeContainsControlFlow(node)) {
            // Pure data conditional (ternary-like): an empty false arm renders no `else`.
            return new IfElseNode(node.Condition, Sequence(node.TrueCase.Accept(this)), Sequence(node.FalseCase.Accept(this)));
        }

        // Control-flow conditional: at least one arm transfers, so it carries the source instruction.
        CfgInstruction instruction = FindArmInstruction(node)
            ?? throw new NotSupportedException("A control-flow conditional has no instruction-bearing arm to resolve typed CFG edges against.");

        IVisitableAstNode trueArm = LowerConditionalArm(instruction, node.TrueCase);
        IVisitableAstNode falseArm = LowerConditionalArm(instruction, node.FalseCase);

        if (!IsEmpty(trueArm)) {
            return new IfElseNode(node.Condition, trueArm, falseArm);
        }

        // The taken arm is empty (e.g. a fallthrough that is the next emitted node). Emit only the
        // remaining arm under the negated condition rather than an empty `if` with a populated `else`.
        if (!IsEmpty(falseArm)) {
            return new IfElseNode(NegatedCondition(node.Condition), falseArm, Empty());
        }

        return Empty();
    }

    private static ValueNode NegatedCondition(ValueNode condition) {
        if (condition is UnaryOperationNode { UnaryOperation: UnaryOperation.NOT } notNode) {
            return notNode.Value;
        }
        return new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, condition);
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
    private IVisitableAstNode LowerConditionalArm(CfgInstruction instruction, IVisitableAstNode arm) => arm switch {
        JumpNearNode jumpNearNode => Sequence(LowerConditionalNearJumpArm(jumpNearNode)),
        MoveIpNextNode => Sequence(LowerFallthroughArm(instruction)),
        BlockNode blockNode => Sequence(blockNode.Statements.Select(statement => LowerConditionalArm(instruction, statement))),
        _ => Sequence(arm.Accept(this))
    };

    private IVisitableAstNode LowerConditionalNearJumpArm(JumpNearNode node) {
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
            return UntestedMessages.Jump(node.Instruction.Address);
        }
        return BuildNearRuntimeDispatch(node.Instruction, node.Ip, "jump",
            edge => Transfer.Emit(edge, CurrentMethod));
    }

    /// <summary>
    /// Lowers a <see cref="MoveIpNextNode"/> fallthrough arm. The fallthrough target is, by construction, the
    /// instruction that statically follows the conditional in memory, so it is resolved directly against that
    /// address. When that edge was never observed during discovery (the conditional was always taken) but the
    /// following block was discovered in this same partition, the edge is synthesized and lowered as a normal
    /// goto; only when no such block exists does the generated code fail as untested.
    /// </summary>
    private IVisitableAstNode LowerFallthroughArm(CfgInstruction instruction) {
        SegmentedAddress fallthroughAddress = instruction.NextInMemoryAddress32.ToSegmentedAddress();
        if (TryResolveConstantTarget(instruction, fallthroughAddress) is ResolvedCfgEdge edge) {
            return Transfer.Emit(edge, CurrentMethod, allowCallOutContinuation: true);
        }
        if (Context.TryResolveSamePartitionBlockEntry(instruction, fallthroughAddress) is ResolvedCfgEdge blockEntryEdge) {
            return Transfer.Emit(blockEntryEdge, CurrentMethod);
        }
        return UntestedMessages.ConditionalFallthrough(instruction.Address);
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

    private IVisitableAstNode BuildNearRuntimeDispatch(CfgInstruction instruction, IVisitableAstNode target, string targetKind,
        Func<ResolvedCfgEdge, IVisitableAstNode> bodyForEdge) {
        if (!TryGetObservedDispatchEdges(instruction, targetKind, out IReadOnlyList<ResolvedCfgEdge> edges, out IVisitableAstNode failure)) {
            return failure;
        }
        ExplicitCastNode switchValue = new(DataType.UINT16, target);
        List<SwitchSection> sections = edges.OrderBy(edge => edge.Target.Address.Offset)
            .Select(edge => Section(edge.Target.Address.Offset, bodyForEdge(edge))).ToList();
        return new SwitchNode(switchValue, sections, Sequence(UntestedMessages.NearTarget(targetKind, switchValue, instruction.Address)));
    }

    private IVisitableAstNode BuildFarRuntimeDispatch(CfgInstruction instruction, ValueNode segment, ValueNode offset, string targetKind,
        Func<ResolvedCfgEdge, IVisitableAstNode> bodyForEdge) {
        if (!TryGetObservedDispatchEdges(instruction, $"far {targetKind}", out IReadOnlyList<ResolvedCfgEdge> edges, out IVisitableAstNode failure)) {
            return failure;
        }
        VariableDeclarationNode segmentDeclaration = new(DataType.UINT16, "targetSegment", segment);
        VariableDeclarationNode offsetDeclaration = new(DataType.UINT16, "targetOffset", offset);
        List<IVisitableAstNode> items = [segmentDeclaration, offsetDeclaration];
        foreach (ResolvedCfgEdge edge in edges.OrderBy(edge => edge.Target.Address.Segment).ThenBy(edge => edge.Target.Address.Offset)) {
            // The matched branch must transfer explicitly (the body uses forceSameMethodGoto) so it never
            // falls through to the trailing untested-target failure even when the target is the next node.
            items.Add(new IfElseNode(
                AddressEquals(segmentDeclaration.Reference, offsetDeclaration.Reference, edge.Target.Address),
                Sequence(bodyForEdge(edge)),
                Empty()));
        }
        items.Add(UntestedMessages.FarTarget(targetKind, segmentDeclaration.Reference, offsetDeclaration.Reference, instruction.Address));
        return Sequence(items);
    }

    /// <summary>
    /// Builds the <c>segment == expectedSegment && offset == 0xOffset</c> test a far dispatch uses to recognize
    /// one observed target.
    /// </summary>
    private static ValueNode AddressEquals(ValueNode segment, ValueNode offset, SegmentedAddress expected) =>
        new BinaryOperationNode(DataType.BOOL,
            new BinaryOperationNode(DataType.BOOL, segment, BinaryOperation.EQUAL, new SegmentVariableNode(expected.Segment)),
            BinaryOperation.LOGICAL_AND,
            new BinaryOperationNode(DataType.BOOL, offset, BinaryOperation.EQUAL, new OffsetLiteralNode(expected.Offset)));

    /// <summary>
    /// Resolves the observed normal successor edges that an indirect (computed) jump or call dispatches over.
    /// Returns <c>false</c> with a runtime <c>FailAsUntested</c> throw in <paramref name="failure"/> when the
    /// instruction was decoded into the CFG but never observed taking any edge during discovery. Failing at
    /// generation time would abort the whole dump; instead the generated code compiles and only fails if this
    /// branch is actually reached at runtime, matching the untested-path convention used elsewhere.
    /// </summary>
    private bool TryGetObservedDispatchEdges(CfgInstruction instruction, string targetKind,
        out IReadOnlyList<ResolvedCfgEdge> edges, out IVisitableAstNode failure) {
        edges = Context.GetSuccessorEdges(instruction, InstructionSuccessorType.Normal);
        if (edges.Count == 0) {
            failure = UntestedMessages.IndirectWithoutTargets(targetKind, instruction.Address);
            return false;
        }
        failure = Empty();
        return true;
    }

    private IVisitableAstNode CallBranchBody(CfgInstruction instruction, CallContinuation continuation,
        string helperName, ResolvedCfgEdge edge, bool isFarCall, bool forceSameMethodGoto) {
        ushort? targetCs = isFarCall ? edge.Target.Address.Segment : null;
        return Sequence(
            Transfer.CallHelper(helperName, continuation.ExpectedReturnAddress, Transfer.FunctionReference(edge), targetCs),
            Transfer.EmitPostCallContinuation(instruction, continuation, CurrentMethod, forceSameMethodGoto: forceSameMethodGoto));
    }

    private static ushort? TryGetConstantWord(IVisitableAstNode node) => node switch {
        ConstantNode constantNode => unchecked((ushort)constantNode.Value),
        _ => null
    };

    // ----------------------------------------------------------------------------------------------------
    // Non-control-flow visit methods. These need no CFG context: they lower a node directly from its own
    // syntax, or are already immutable expressions that pass through unchanged.
    // ----------------------------------------------------------------------------------------------------

    public IVisitableAstNode VisitBlockNode(BlockNode node) =>
        Sequence(node.Statements.Select(statement => statement.Accept(this)));

    public IVisitableAstNode VisitMoveIpNextNode(MoveIpNextNode node) => Empty();

    public IVisitableAstNode VisitWhileNode(WhileNode node) =>
        new WhileNode(node.Condition, Sequence(node.Body.Accept(this)));

    public IVisitableAstNode VisitHltNode(HltNode node) => new ReturnNode(new MethodCallNode(null, "Hlt"));

    public IVisitableAstNode VisitInvalidInstructionNode(InvalidInstructionNode node) =>
        new ThrowNode(node.CpuException.GetType(), node.CpuException.Message);

    public IVisitableAstNode VisitCpuidNode(CpuidNode node) =>
        new ThrowNode(typeof(CpuInvalidOpcodeException), "Attempted to call CPUID, which is unsupported on CPUs < 486");

    public IVisitableAstNode VisitThrowNode(ThrowNode node) => node;

    public IVisitableAstNode VisitReturnNearNode(ReturnNearNode node) =>
        new ReturnNode(RetCall(node.RetBitWidth == BitWidth.WORD_16 ? "NearRet" : "NearRet32", node.BytesToPop));

    public IVisitableAstNode VisitReturnFarNode(ReturnFarNode node) =>
        new ReturnNode(RetCall(node.RetBitWidth == BitWidth.WORD_16 ? "FarRet" : "FarRet32", node.BytesToPop));

    public IVisitableAstNode VisitReturnInterruptNode(ReturnInterruptNode node) =>
        new ReturnNode(new MethodCallNode(null, "InterruptRet"));

    public IVisitableAstNode VisitInstructionNode(InstructionNode node) => throw Unsupported(node);

    private static MethodCallNode RetCall(string helperName, IVisitableAstNode bytesToPop) {
        if (bytesToPop is ConstantNode { Value: 0 }) {
            return new MethodCallNode(null, helperName);
        }
        return new MethodCallNode(null, helperName, new ExplicitCastNode(DataType.UINT16, bytesToPop));
    }

    // ----------------------------------------------------------------------------------------------------
    // Expression visit methods. They are immutable records, so the lowerer returns them unchanged and
    // printing is entirely delegated to CSharpExpressionPrinter.
    // ----------------------------------------------------------------------------------------------------

    public IVisitableAstNode VisitMethodCallNode(MethodCallNode node) => node;
    public IVisitableAstNode VisitMethodCallValueNode(MethodCallValueNode node) => node;
    public IVisitableAstNode VisitRegisterNode(RegisterNode node) => node;
    public IVisitableAstNode VisitSegmentRegisterNode(SegmentRegisterNode node) => node;
    public IVisitableAstNode VisitSegmentedPointer(SegmentedPointerNode node) => node;
    public IVisitableAstNode VisitAbsolutePointerNode(AbsolutePointerNode node) => node;
    public IVisitableAstNode VisitConstantNode(ConstantNode node) => node;
    public IVisitableAstNode VisitNearAddressNode(NearAddressNode node) => node;
    public IVisitableAstNode VisitBinaryOperationNode(BinaryOperationNode node) => node;
    public IVisitableAstNode VisitUnaryOperationNode(UnaryOperationNode node) => node;
    public IVisitableAstNode VisitTypeConversionNode(TypeConversionNode node) => node;
    public IVisitableAstNode VisitFlagRegisterNode(FlagRegisterNode node) => node;
    public IVisitableAstNode VisitCpuFlagNode(CpuFlagNode node) => node;
    public IVisitableAstNode VisitInstructionFieldNode(InstructionFieldNode node) => node;
    public IVisitableAstNode VisitVariableReferenceNode(VariableReferenceNode node) => node;
    public IVisitableAstNode VisitVariableDeclarationNode(VariableDeclarationNode node) => node;
    public IVisitableAstNode VisitSegmentedAddressNode(SegmentedAddressNode node) => node;

    private static NotSupportedException Unsupported(IVisitableAstNode node) =>
        new($"CFG C# generation does not support AST node {node.GetType().FullName} yet.");
}
