namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
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

    // ----------------------------------------------------------------------------------------------------
    // Per-instruction driving entry. The lowering itself is uniform Accept dispatch; this method only adds
    // the instruction-level fallthrough transfer (the one inter-instruction concern the AST node cannot see).
    // ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lowers the body of an instruction for IR lifting: the division tag when the body matches, otherwise
    /// plain <c>Accept</c> lowering, then the after-check and the fallthrough transfer. No rewrites run:
    /// the IR passes own every rewrite.
    /// </summary>
    /// <param name="node">The instruction's execution AST.</param>
    public BlockNode LowerForIr(CfgInstruction instruction, IVisitableAstNode node, bool emitExternalEventCheckAfter) {
        IVisitableAstNode body = DivisionPattern.TryMatch(node, instruction.Address) is DivisionPattern division ? new DivisionNode(division) : node.Accept(this);
        if (TerminatesControlFlow(node) || !CompletesNormallyVisitor.CompletesNormally(body)) {
            return Sequence(body);
        }
        IVisitableAstNode eventCheck = Empty();
        if (emitExternalEventCheckAfter) {
            eventCheck = new CheckExternalEventsNode(new SegmentedAddress(instruction.Address.Segment, (ushort)instruction.NextInMemoryAddress32.Offset));
        }
        return Sequence(body, eventCheck, Transfer.EmitFallthroughIfNeeded(instruction));
    }

    private static bool TerminatesControlFlow(IVisitableAstNode node) =>
        NodeContainsControlFlow(node) || node is InvalidInstructionNode;

    private static bool NodeContainsControlFlow(IVisitableAstNode node) {
        Stack<IVisitableAstNode> pending = new();
        pending.Push(node);
        while (pending.Count > 0) {
            switch (pending.Pop()) {
                case BlockNode blockNode:
                    foreach (IVisitableAstNode statement in blockNode.Statements) {
                        pending.Push(statement);
                    }
                    break;
                case IfElseNode ifElseNode:
                    pending.Push(ifElseNode.TrueCase);
                    pending.Push(ifElseNode.FalseCase);
                    break;
                case JumpNearNode or JumpFarNode or CallNearNode or CallFarNode or InterruptCallNode
                    or CallbackNode or ReturnNearNode or ReturnFarNode or ReturnInterruptNode:
                    return true;
            }
        }
        return false;
    }

    // ----------------------------------------------------------------------------------------------------
    // Control-flow visit methods. These consult the CFG generation context and the transfer emitter.
    // ----------------------------------------------------------------------------------------------------

    public IVisitableAstNode VisitJumpNearNode(JumpNearNode node) {
        if (TryGetConstantWord(node.Ip) is ushort offset) {
            return Transfer.Emit(Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
                new SegmentedAddress(node.Instruction.Address.Segment, offset)));
        }

        return BuildNearRuntimeDispatch(node.Instruction, node.Ip, "jump",
            edge => Transfer.Emit(edge));
    }

    public IVisitableAstNode VisitJumpFarNode(JumpFarNode node) {
        ushort? segment = TryGetConstantWord(node.TargetAddress.Segment);
        ushort? offset = TryGetConstantWord(node.TargetAddress.Offset);
        if (segment is not null && offset is not null) {
            return Transfer.Emit(Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
                new SegmentedAddress(segment.Value, offset.Value)));
        }

        return BuildFarRuntimeDispatch(node.Instruction, node.TargetAddress.Segment, node.TargetAddress.Offset, "jump",
            // Every matched branch ends with its own transfer, so it never falls through to the trailing untested-target failure.
            edge => Transfer.Emit(edge));
    }

    public IVisitableAstNode VisitCallNearNode(CallNearNode node) {
        if (TryGetConstantWord(node.TargetIp) is not ushort targetOffset) {
            CallContinuation runtimeContinuation = Context.ResolveCallContinuation(node.Instruction);
            return BuildNearRuntimeDispatch(node.Instruction, node.TargetIp, "call",
                edge => CallBranchBody(node.Instruction, runtimeContinuation, CallDistance.Near, node.CallBitWidth, edge));
        }

        ResolvedCfgEdge targetEdge = Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal,
            new SegmentedAddress(node.Instruction.Address.Segment, targetOffset));
        return Transfer.EmitCallHelperAndContinuation(CallDistance.Near, node.CallBitWidth, node.Instruction, Transfer.FunctionReference(targetEdge), null);
    }

    public IVisitableAstNode VisitCallFarNode(CallFarNode node) {
        ushort? targetSegment = TryGetConstantWord(node.TargetAddress.Segment);
        ushort? targetOffset = TryGetConstantWord(node.TargetAddress.Offset);
        if (targetSegment is null || targetOffset is null) {
            CallContinuation runtimeContinuation = Context.ResolveCallContinuation(node.Instruction);
            return BuildFarCallDispatch(node.Instruction, node.TargetAddress.Segment, node.TargetAddress.Offset,
                CallDistance.Far, node.CallBitWidth, runtimeContinuation);
        }

        SegmentedAddress targetAddress = new SegmentedAddress(targetSegment.Value, targetOffset.Value);
        ResolvedCfgEdge targetEdge = Context.ResolveEdge(node.Instruction, InstructionSuccessorType.Normal, targetAddress);
        return Transfer.EmitCallHelperAndContinuation(CallDistance.Far, node.CallBitWidth, node.Instruction, Transfer.FunctionReference(targetEdge), targetSegment.Value);
    }

    public IVisitableAstNode VisitInterruptCallNode(InterruptCallNode node) {
        CallContinuation continuation = Context.ResolveCallContinuation(node.Instruction);
        SegmentedAddress expectedReturn = continuation.ExpectedReturnAddress;
        return Sequence(
            new InterruptCallHelperNode(
                new SegmentVariableNode(expectedReturn.Segment),
                new OffsetLiteralNode(expectedReturn.Offset),
                new ExplicitCastNode(DataType.UINT8, node.VectorNumber)),
            Transfer.EmitPostCallContinuation(node.Instruction, continuation));
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
            Transfer.EmitFallthroughIfNeeded(node.Instruction));
    }

    /// <summary>
    /// Lowers a callback whose handler kept CS:IP at the callback during discovery: a direct
    /// <c>Callback</c> call that returns into the emitted fallthrough.
    /// </summary>
    private static MethodCallNode EmitDirectCallback(CallbackNode node) =>
        new CallbackHelperNode(
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
                Sequence(Transfer.Emit(edge)),
                Empty()));
        }
        targets.Add(UntestedMessages.CallbackTarget(node.Instruction.Address));
        MethodCallValueNode movedCsIp = new CallbackMovedCsIpHelperNode(
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
            MethodCallValueNode signatureMatches = new SignatureMatchesHelperNode(
                new SegmentVariableNode(selectorNode.Address.Segment),
                new OffsetLiteralNode(selectorNode.Address.Offset),
                new SignatureFieldNode(Context.GetSignatureField(target)));
            items.Add(new IfElseNode(signatureMatches,
                Sequence(Transfer.Emit(edge)),
                Empty()));
        }
        items.Add(UntestedMessages.CodeVariant(selectorNode.Address));
        return Sequence(items);
    }

    // ----------------------------------------------------------------------------------------------------
    // Conditional control flow. Both arms of a control-flow conditional are lowered to explicit transfers;
    // FallthroughGotoRemover later drops the arm that reaches the next node and negates the condition when the
    // taken arm is the one that became empty.
    // ----------------------------------------------------------------------------------------------------

    public IVisitableAstNode VisitIfElseNode(IfElseNode node) {
        if (IsSetInterruptShadow(node.TrueCase) && IsEmpty(node.FalseCase)) {
            return Empty();
        }
        if (!NodeContainsControlFlow(node)) {
            // Pure data conditional (ternary-like): an empty false arm renders no `else`.
            return new IfElseNode(node.Condition, Sequence(node.TrueCase.Accept(this)), Sequence(node.FalseCase.Accept(this)));
        }

        // Control-flow conditional: at least one arm transfers, so it carries the source instruction.
        CfgInstruction instruction = FindArmInstruction(node)
            ?? throw new NotSupportedException("A control-flow conditional has no instruction-bearing arm to resolve typed CFG edges against.");

        IVisitableAstNode trueArm = LowerConditionalArm(instruction, node.TrueCase);
        IVisitableAstNode falseArm = LowerConditionalArm(instruction, node.FalseCase);
        return new IfElseNode(node.Condition, trueArm, falseArm);
    }

    private static CfgInstruction? FindArmInstruction(IfElseNode node) =>
        FindArmInstruction(node.TrueCase) ?? FindArmInstruction(node.FalseCase);

    private static CfgInstruction? FindArmInstruction(IVisitableAstNode arm) => arm switch {
        CfgInstructionNode instructionNode => instructionNode.Instruction,
        BlockNode blockNode => blockNode.Statements.Select(FindArmInstruction).FirstOrDefault(found => found is not null),
        _ => null
    };

    /// <summary>
    /// Whether <paramref name="node"/> only sets <c>State.InterruptShadowing</c>: the <c>if (!InterruptFlag)</c> form comes
    /// from <c>sti</c> and the plain node from <c>mov ss</c> / <c>lss</c>. Generated code drops it: its event checks
    /// never read the flag, which would only reach the interpreter stale when control returns to it.
    /// </summary>
    private static bool IsSetInterruptShadow(IVisitableAstNode node) => node switch {
        SetInterruptShadowNode => true,
        BlockNode { Statements: [IVisitableAstNode single] } => IsSetInterruptShadow(single),
        _ => false
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
        if (TryGetConstantWord(node.Ip) is not null) {
            // Constant target: transfer along the CFG edge, or fail as untested when discovery (observed or
            // speculative) never produced it.
            if (TryResolveNearJump(node) is ResolvedCfgEdge edge) {
                return Transfer.Emit(edge);
            }
            return UntestedMessages.Jump(node.Instruction.Address);
        }
        return BuildNearRuntimeDispatch(node.Instruction, node.Ip, "jump",
            edge => Transfer.Emit(edge));
    }

    /// <summary>
    /// Lowers a <see cref="MoveIpNextNode"/> fallthrough arm: the CFG edge to the instruction that statically follows
    /// the conditional in memory, or a <c>FailAsUntested</c> throw when discovery never produced that edge.
    /// </summary>
    private IVisitableAstNode LowerFallthroughArm(CfgInstruction instruction) {
        SegmentedAddress fallthroughAddress = instruction.NextInMemoryAddress32.ToSegmentedAddress();
        if (TryResolveConstantTarget(instruction, fallthroughAddress) is ResolvedCfgEdge edge) {
            return Transfer.Emit(edge, allowCallOutContinuation: true);
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
            // Every matched branch ends with its own transfer, so it never falls through to the trailing untested-target failure.
            items.Add(new IfElseNode(
                AddressEquals(segmentDeclaration.Reference, offsetDeclaration.Reference, edge.Target.Address),
                Sequence(bodyForEdge(edge)),
                Empty()));
        }
        items.Add(UntestedMessages.FarTarget(targetKind, segmentDeclaration.Reference, offsetDeclaration.Reference, instruction.Address));
        return Sequence(items);
    }

    /// <summary>
    /// Lowers a far call through a runtime target: a single observed target keeps an <c>if</c> / <c>else</c>
    /// that only calls it and whose <c>else</c> is the untested-target failure; two or more targets become a
    /// <c>switch</c> on the 32-bit segmented address <c>(segment &lt;&lt; 16) | offset</c> with one section
    /// per target that only calls it and the untested-target failure as the default. Every target shares the
    /// call continuation, so it follows the dispatch once instead of ending each arm.
    /// </summary>
    private IVisitableAstNode BuildFarCallDispatch(CfgInstruction instruction, ValueNode segment, ValueNode offset,
        CallDistance distance, BitWidth width, CallContinuation continuation) {
        if (!TryGetObservedDispatchEdges(instruction, "far call", out IReadOnlyList<ResolvedCfgEdge> edges, out IVisitableAstNode failure)) {
            return failure;
        }
        VariableDeclarationNode segmentDeclaration = new(DataType.UINT16, "targetSegment", segment);
        VariableDeclarationNode offsetDeclaration = new(DataType.UINT16, "targetOffset", offset);
        if (edges.Count == 1) {
            ResolvedCfgEdge only = edges[0];
            return Sequence(segmentDeclaration, offsetDeclaration,
                new IfElseNode(
                    AddressEquals(segmentDeclaration.Reference, offsetDeclaration.Reference, only.Target.Address),
                    Sequence(Transfer.CallHelper(distance, width, continuation.ExpectedReturnAddress,
                        Transfer.FunctionReference(only), only.Target.Address.Segment)),
                    Sequence(UntestedMessages.FarTarget("call", segmentDeclaration.Reference,
                        offsetDeclaration.Reference, instruction.Address))),
                Transfer.EmitPostCallContinuation(instruction, continuation));
        }
        ExplicitCastNode switchValue = new(DataType.UINT32,
            new BinaryOperationNode(DataType.INT32,
                new BinaryOperationNode(DataType.INT32, segmentDeclaration.Reference, BinaryOperation.LEFT_SHIFT,
                    new ConstantNode(DataType.INT32, 16)),
                BinaryOperation.BITWISE_OR,
                offsetDeclaration.Reference));
        List<SwitchSection> sections = [];
        HashSet<uint> seen = [];
        foreach (ResolvedCfgEdge edge in edges.OrderBy(edge => edge.Target.Address.Segment).ThenBy(edge => edge.Target.Address.Offset)) {
            uint key = ((uint)edge.Target.Address.Segment << 16) | edge.Target.Address.Offset;
            if (!seen.Add(key)) {
                throw new InvalidOperationException(
                    $"Far call dispatch at {instruction.Address} has two edges to {edge.Target.Address}.");
            }
            sections.Add(Section(key,
                Sequence(Transfer.CallHelper(distance, width, continuation.ExpectedReturnAddress,
                    Transfer.FunctionReference(edge), edge.Target.Address.Segment))));
        }
        SwitchNode dispatch = new(switchValue, sections,
            Sequence(UntestedMessages.FarTarget("call", segmentDeclaration.Reference, offsetDeclaration.Reference,
                instruction.Address)));
        return Sequence(segmentDeclaration, offsetDeclaration, dispatch,
            Transfer.EmitPostCallContinuation(instruction, continuation));
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
        CallDistance distance, BitWidth width, ResolvedCfgEdge edge) {
        return Sequence(
            Transfer.CallHelper(distance, width, continuation.ExpectedReturnAddress, Transfer.FunctionReference(edge), null),
            Transfer.EmitPostCallContinuation(instruction, continuation));
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

    public IVisitableAstNode VisitWhileWithExitNode(WhileWithExitNode node) =>
        new WhileNode(node.Condition, Sequence(node.Body.Accept(this), new IfElseNode(node.ExitCondition, new BlockNode(new BreakNode()), Empty())));

    public IVisitableAstNode VisitHltNode(HltNode node) => new ReturnNode(new HltHelperNode());

    public IVisitableAstNode VisitInvalidInstructionNode(InvalidInstructionNode node) =>
        new ThrowNode(node.CpuException.GetType(), node.CpuException.Message);

    public IVisitableAstNode VisitCpuidNode(CpuidNode node) =>
        new ThrowNode(typeof(CpuInvalidOpcodeException), "Attempted to call CPUID, which is unsupported on CPUs < 486");

    public IVisitableAstNode VisitThrowNode(ThrowNode node) => node;

    public IVisitableAstNode VisitReturnNearNode(ReturnNearNode node) =>
        new ReturnNode(RetCall(CallDistance.Near, node.RetBitWidth, node.BytesToPop));

    public IVisitableAstNode VisitReturnFarNode(ReturnFarNode node) =>
        new ReturnNode(RetCall(CallDistance.Far, node.RetBitWidth, node.BytesToPop));

    public IVisitableAstNode VisitReturnInterruptNode(ReturnInterruptNode node) =>
        new InterruptReturnNode(node.OperandSize);

    public IVisitableAstNode VisitInstructionNode(InstructionNode node) => throw Unsupported(node);

    private static MethodCallNode RetCall(CallDistance distance, BitWidth width, IVisitableAstNode bytesToPop) {
        if (bytesToPop is ConstantNode { Value: 0 }) {
            return new ProcedureReturnHelperNode(distance, width);
        }
        return new ProcedureReturnHelperNode(distance, width, new ExplicitCastNode(DataType.UINT16, bytesToPop));
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
    public IVisitableAstNode VisitSetInterruptShadowNode(SetInterruptShadowNode node) => Empty();
    public IVisitableAstNode VisitUnaryOperationNode(UnaryOperationNode node) => node;
    public IVisitableAstNode VisitConditionalValueNode(ConditionalValueNode node) => node;
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
