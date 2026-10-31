namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Collections.Immutable;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Collects the labels a lowered tree jumps to, plus whether it re-enters the entry dispatcher. Runtime
/// control-flow nodes are rejected: a lowered tree only carries code-generation nodes, so seeing one is a
/// generator bug.
/// </summary>
internal sealed class GotoTargetCollector : ICodeGenerationAstVisitor<GotoTargets> {
    private static GotoTargetCollector Instance { get; } = new();

    private GotoTargetCollector() {
    }

    /// <summary>The jump targets of <paramref name="node"/>: its <see cref="GotoNode"/> labels and whether
    /// a <see cref="CyclicJumpNode"/> re-enters the entry dispatcher. Container nodes are walked on an
    /// explicit stack so deeply nested <c>if</c> / <c>else</c> chains cannot overflow the thread's stack.</summary>
    public static GotoTargets Collect(IVisitableAstNode node) {
        HashSet<IGotoTarget> labels = [];
        bool entryDispatcher = false;
        Stack<IVisitableAstNode> pending = new();
        pending.Push(node);
        while (pending.Count > 0) {
            IVisitableAstNode current = pending.Pop();
            switch (current) {
                case BlockNode block:
                    foreach (IVisitableAstNode statement in block.Statements) {
                        pending.Push(statement);
                    }
                    continue;
                case IfElseNode ifElse:
                    pending.Push(ifElse.TrueCase);
                    pending.Push(ifElse.FalseCase);
                    continue;
                case WhileNode or InfiniteLoopNode or DoWhileNode
                    or PreTestLoopNode or CpuFaultTryCatchNode or InstructionBoundaryNode:
                    pending.Push(ChildStatements(current).Single());
                    continue;
                case WhileWithExitNode whileWithExit:
                    pending.Push(whileWithExit.Body);
                    continue;
                case SwitchNode switchNode:
                    foreach (SwitchSection section in switchNode.Sections) {
                        pending.Push(section.Body);
                    }
                    pending.Push(switchNode.Default);
                    continue;
                case OpaqueNode opaque:
                    pending.Push(opaque.Original);
                    continue;
            }
            GotoTargets found = current.Accept(Instance);
            labels.UnionWith(found.Labels);
            entryDispatcher |= found.EntryDispatcher;
        }
        return new GotoTargets(labels.ToImmutableHashSet(), entryDispatcher);
    }

    public GotoTargets VisitBlockNode(BlockNode node) => Union(node.Statements);

    public GotoTargets VisitIfElseNode(IfElseNode node) =>
        Collect(node.TrueCase).Union(Collect(node.FalseCase));

    public GotoTargets VisitWhileNode(WhileNode node) => Collect(node.Body);

    public GotoTargets VisitWhileWithExitNode(WhileWithExitNode node) => Collect(node.Body);

    public GotoTargets VisitInfiniteLoopNode(InfiniteLoopNode node) => Collect(node.Body);

    public GotoTargets VisitDoWhileNode(DoWhileNode node) => Collect(node.Body);

    public GotoTargets VisitSwitchNode(SwitchNode node) {
        GotoTargets targets = GotoTargets.None;
        foreach (SwitchSection section in node.Sections) {
            targets = targets.Union(Collect(section.Body));
        }
        return targets.Union(Collect(node.Default));
    }

    public GotoTargets VisitCpuFaultTryCatchNode(CpuFaultTryCatchNode node) => Collect(node.Body);

    public GotoTargets VisitInstructionBoundaryNode(InstructionBoundaryNode node) => Collect(node.Body);

    public GotoTargets VisitGotoNode(GotoNode node) => new(ImmutableHashSet.Create(node.Target), false);

    public GotoTargets VisitCyclicJumpNode(CyclicJumpNode node) => new(ImmutableHashSet<IGotoTarget>.Empty, true);

    public GotoTargets VisitLabelNode(LabelNode node) => GotoTargets.None;

    public GotoTargets VisitEntryDispatcherLabelNode(EntryDispatcherLabelNode node) => GotoTargets.None;

    public GotoTargets VisitReturnNode(ReturnNode node) => GotoTargets.None;

    public GotoTargets VisitBreakNode(BreakNode node) => GotoTargets.None;

    public GotoTargets VisitContinueNode(ContinueNode node) => GotoTargets.None;

    public GotoTargets VisitFailAsUntestedNode(FailAsUntestedNode node) => GotoTargets.None;

    public GotoTargets VisitCommentNode(CommentNode node) => GotoTargets.None;

    public GotoTargets VisitCheckExternalEventsNode(CheckExternalEventsNode node) => GotoTargets.None;

    public GotoTargets VisitSpeculativeGuardNode(SpeculativeGuardNode node) => GotoTargets.None;

    public GotoTargets VisitDivisionNode(DivisionNode node) => GotoTargets.None;

    public GotoTargets VisitSegmentVariableNode(SegmentVariableNode node) => GotoTargets.None;

    public GotoTargets VisitOffsetLiteralNode(OffsetLiteralNode node) => GotoTargets.None;

    public GotoTargets VisitInstructionPointerNode(InstructionPointerNode node) => GotoTargets.None;

    public GotoTargets VisitLoadOffsetParameterNode(LoadOffsetParameterNode node) => GotoTargets.None;

    public GotoTargets VisitMethodGroupNode(MethodGroupNode node) => GotoTargets.None;

    public GotoTargets VisitSignatureFieldNode(SignatureFieldNode node) => GotoTargets.None;

    public GotoTargets VisitExplicitCastNode(ExplicitCastNode node) => GotoTargets.None;

    public GotoTargets VisitInstructionFieldNode(InstructionFieldNode node) => GotoTargets.None;

    public GotoTargets VisitSegmentRegisterNode(SegmentRegisterNode node) => GotoTargets.None;

    public GotoTargets VisitSegmentedPointer(SegmentedPointerNode node) => GotoTargets.None;

    public GotoTargets VisitRegisterNode(RegisterNode node) => GotoTargets.None;

    public GotoTargets VisitAbsolutePointerNode(AbsolutePointerNode node) => GotoTargets.None;

    public GotoTargets VisitSegmentedAddressNode(SegmentedAddressNode node) => GotoTargets.None;

    public GotoTargets VisitBinaryOperationNode(BinaryOperationNode node) => GotoTargets.None;

    public GotoTargets VisitUnaryOperationNode(UnaryOperationNode node) => GotoTargets.None;

    public GotoTargets VisitConditionalValueNode(ConditionalValueNode node) => GotoTargets.None;

    public GotoTargets VisitTypeConversionNode(TypeConversionNode node) => GotoTargets.None;

    public GotoTargets VisitConstantNode(ConstantNode node) => GotoTargets.None;

    public GotoTargets VisitNearAddressNode(NearAddressNode node) => GotoTargets.None;

    public GotoTargets VisitMethodCallNode(MethodCallNode node) => GotoTargets.None;

    public GotoTargets VisitMethodCallValueNode(MethodCallValueNode node) => GotoTargets.None;

    public GotoTargets VisitThrowNode(ThrowNode node) => GotoTargets.None;

    public GotoTargets VisitSetInterruptShadowNode(SetInterruptShadowNode node) => GotoTargets.None;

    public GotoTargets VisitCpuFlagNode(CpuFlagNode node) => GotoTargets.None;

    public GotoTargets VisitFlagRegisterNode(FlagRegisterNode node) => GotoTargets.None;

    public GotoTargets VisitVariableReferenceNode(VariableReferenceNode node) => GotoTargets.None;

    public GotoTargets VisitVariableDeclarationNode(VariableDeclarationNode node) => GotoTargets.None;

    public GotoTargets VisitInstructionNode(InstructionNode node) => Unlowered(node);

    public GotoTargets VisitMoveIpNextNode(MoveIpNextNode node) => Unlowered(node);

    public GotoTargets VisitCallNearNode(CallNearNode node) => Unlowered(node);

    public GotoTargets VisitCallFarNode(CallFarNode node) => Unlowered(node);

    public GotoTargets VisitReturnNearNode(ReturnNearNode node) => Unlowered(node);

    public GotoTargets VisitReturnFarNode(ReturnFarNode node) => Unlowered(node);

    public GotoTargets VisitReturnInterruptNode(ReturnInterruptNode node) => Unlowered(node);

    public GotoTargets VisitJumpNearNode(JumpNearNode node) => Unlowered(node);

    public GotoTargets VisitJumpFarNode(JumpFarNode node) => Unlowered(node);

    public GotoTargets VisitHltNode(HltNode node) => Unlowered(node);

    public GotoTargets VisitInterruptCallNode(InterruptCallNode node) => Unlowered(node);

    public GotoTargets VisitCallbackNode(CallbackNode node) => Unlowered(node);

    public GotoTargets VisitSelectorNode(SelectorNode node) => Unlowered(node);

    public GotoTargets VisitInvalidInstructionNode(InvalidInstructionNode node) => Unlowered(node);

    public GotoTargets VisitCpuidNode(CpuidNode node) => Unlowered(node);

    private GotoTargets Union(IReadOnlyList<IVisitableAstNode> nodes) {
        GotoTargets targets = GotoTargets.None;
        foreach (IVisitableAstNode node in nodes) {
            targets = targets.Union(Collect(node));
        }
        return targets;
    }

    private static GotoTargets Unlowered(IVisitableAstNode node) =>
        throw new InvalidOperationException($"{node.GetType().Name} must be lowered before goto target analysis.");

    public GotoTargets VisitTupleSwapNode(TupleSwapNode node) => GotoTargets.None;

    public GotoTargets VisitAluOperationNode(AluOperationNode node) => node.Call.Accept(this);
    public GotoTargets VisitAluStatementNode(AluStatementNode node) => node.Call.Accept(this);
    public GotoTargets VisitOpaqueNode(OpaqueNode node) => node.Original.Accept(this);
    public GotoTargets VisitInstructionStartNode(InstructionStartNode node) => GotoTargets.None;
    public GotoTargets VisitInterruptReturnNode(InterruptReturnNode node) => GotoTargets.None;
    public GotoTargets VisitPreTestLoopNode(PreTestLoopNode node) => Collect(node.Body);
    public GotoTargets VisitLocalDeclarationNode(LocalDeclarationNode node) => GotoTargets.None;
}
