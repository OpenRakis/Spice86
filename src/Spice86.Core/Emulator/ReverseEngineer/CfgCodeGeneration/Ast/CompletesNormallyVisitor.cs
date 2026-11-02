namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using System.Linq;

/// <summary>
/// Computes whether control can reach the statement after a lowered node. Runtime control-flow nodes are
/// rejected: a lowered tree only carries code-generation nodes, so seeing one is a generator bug.
/// </summary>
internal sealed class CompletesNormallyVisitor : ICodeGenerationAstVisitor<bool> {
    private static CompletesNormallyVisitor Instance { get; } = new();

    private CompletesNormallyVisitor() {
    }

    /// <summary>Whether control can reach the statement after <paramref name="node"/>. Container nodes are
    /// walked through <see cref="AstTrampoline"/> so arbitrarily deep <c>if</c> / <c>else</c> chains cannot
    /// overflow the thread's stack; leaf nodes go through the visitor as before.</summary>
    public static bool CompletesNormally(IVisitableAstNode node) {
        AstResult<bool> result = new();
        AstTrampoline.Run(Instance.Eval(node, result));
        return result.Value;
    }

    private IEnumerable<AstSubcall> Eval(IVisitableAstNode node, AstResult<bool> result) {
        switch (node) {
            case BlockNode block:
                if (block.Statements.Count == 0) {
                    result.Value = true;
                    yield break;
                }
                yield return AstTrampoline.Call(Eval(block.Statements[^1], result));
                yield break;
            case IfElseNode ifElse: {
                AstResult<bool> whenTrue = new();
                yield return AstTrampoline.Call(Eval(ifElse.TrueCase, whenTrue));
                if (whenTrue.Value) {
                    result.Value = true;
                    yield break;
                }
                yield return AstTrampoline.Call(Eval(ifElse.FalseCase, result));
                yield break;
            }
            case DoWhileNode doWhile: {
                AstResult<bool> body = new();
                yield return AstTrampoline.Call(Eval(doWhile.Body, body));
                result.Value = body.Value || LoopExits.HasBreak(doWhile.Body)
                    || LoopExits.HasContinue(doWhile.Body);
                yield break;
            }
            case SwitchNode switchNode:
                if (switchNode.Sections.Any(section => LoopExits.HasBreak(section.Body))) {
                    result.Value = true;
                    yield break;
                }
                yield return AstTrampoline.Call(Eval(switchNode.Default, result));
                yield break;
            case CpuFaultTryCatchNode tryCatch:
                yield return AstTrampoline.Call(Eval(tryCatch.Body, result));
                yield break;
            case InstructionBoundaryNode boundary:
                yield return AstTrampoline.Call(Eval(boundary.Body, result));
                yield break;
            default:
                result.Value = node.Accept(this);
                yield break;
        }
    }

    public bool VisitBlockNode(BlockNode node) {
        if (node.Statements.Count == 0) {
            return true;
        }
        return CompletesNormally(node.Statements[^1]);
    }

    public bool VisitIfElseNode(IfElseNode node) =>
        CompletesNormally(node.TrueCase) || CompletesNormally(node.FalseCase);

    public bool VisitWhileNode(WhileNode node) => true;

    public bool VisitWhileWithExitNode(WhileWithExitNode node) => true;

    public bool VisitInfiniteLoopNode(InfiniteLoopNode node) => LoopExits.HasBreak(node.Body);

    public bool VisitDoWhileNode(DoWhileNode node) =>
        CompletesNormally(node.Body) || LoopExits.HasBreak(node.Body) || LoopExits.HasContinue(node.Body);

    public bool VisitSwitchNode(SwitchNode node) =>
        node.Sections.Any(section => LoopExits.HasBreak(section.Body)) || CompletesNormally(node.Default);

    public bool VisitCpuFaultTryCatchNode(CpuFaultTryCatchNode node) => CompletesNormally(node.Body);

    public bool VisitInstructionBoundaryNode(InstructionBoundaryNode node) => CompletesNormally(node.Body);

    public bool VisitReturnNode(ReturnNode node) => false;

    public bool VisitGotoNode(GotoNode node) => false;

    public bool VisitThrowNode(ThrowNode node) => false;

    public bool VisitSetInterruptShadowNode(SetInterruptShadowNode node) => true;

    public bool VisitFailAsUntestedNode(FailAsUntestedNode node) => false;

    public bool VisitCyclicJumpNode(CyclicJumpNode node) => false;

    public bool VisitBreakNode(BreakNode node) => false;

    public bool VisitContinueNode(ContinueNode node) => false;

    public bool VisitLabelNode(LabelNode node) => true;

    public bool VisitEntryDispatcherLabelNode(EntryDispatcherLabelNode node) => true;

    public bool VisitCommentNode(CommentNode node) => true;

    public bool VisitCheckExternalEventsNode(CheckExternalEventsNode node) => true;

    public bool VisitSpeculativeGuardNode(SpeculativeGuardNode node) => true;

    public bool VisitDivisionNode(DivisionNode node) => true;

    public bool VisitInstructionNode(InstructionNode node) => Unlowered(node);

    public bool VisitMoveIpNextNode(MoveIpNextNode node) => Unlowered(node);

    public bool VisitCallNearNode(CallNearNode node) => Unlowered(node);

    public bool VisitCallFarNode(CallFarNode node) => Unlowered(node);

    public bool VisitReturnNearNode(ReturnNearNode node) => Unlowered(node);

    public bool VisitReturnFarNode(ReturnFarNode node) => Unlowered(node);

    public bool VisitReturnInterruptNode(ReturnInterruptNode node) => Unlowered(node);

    public bool VisitJumpNearNode(JumpNearNode node) => Unlowered(node);

    public bool VisitJumpFarNode(JumpFarNode node) => Unlowered(node);

    public bool VisitHltNode(HltNode node) => Unlowered(node);

    public bool VisitInterruptCallNode(InterruptCallNode node) => Unlowered(node);

    public bool VisitCallbackNode(CallbackNode node) => Unlowered(node);

    public bool VisitSelectorNode(SelectorNode node) => Unlowered(node);

    public bool VisitInvalidInstructionNode(InvalidInstructionNode node) => Unlowered(node);

    public bool VisitCpuidNode(CpuidNode node) => Unlowered(node);

    public bool VisitInstructionFieldNode(InstructionFieldNode node) => true;

    public bool VisitSegmentRegisterNode(SegmentRegisterNode node) => true;

    public bool VisitSegmentedPointer(SegmentedPointerNode node) => true;

    public bool VisitRegisterNode(RegisterNode node) => true;

    public bool VisitAbsolutePointerNode(AbsolutePointerNode node) => true;

    public bool VisitSegmentedAddressNode(SegmentedAddressNode node) => true;

    public bool VisitBinaryOperationNode(BinaryOperationNode node) => true;

    public bool VisitUnaryOperationNode(UnaryOperationNode node) => true;

    public bool VisitConditionalValueNode(ConditionalValueNode node) => true;

    public bool VisitTypeConversionNode(TypeConversionNode node) => true;

    public bool VisitConstantNode(ConstantNode node) => true;

    public bool VisitNearAddressNode(NearAddressNode node) => true;

    public bool VisitMethodCallNode(MethodCallNode node) => true;

    public bool VisitMethodCallValueNode(MethodCallValueNode node) => true;

    public bool VisitCpuFlagNode(CpuFlagNode node) => true;

    public bool VisitFlagRegisterNode(FlagRegisterNode node) => true;

    public bool VisitVariableReferenceNode(VariableReferenceNode node) => true;

    public bool VisitVariableDeclarationNode(VariableDeclarationNode node) => true;

    public bool VisitSegmentVariableNode(SegmentVariableNode node) => true;

    public bool VisitOffsetLiteralNode(OffsetLiteralNode node) => true;

    public bool VisitInstructionPointerNode(InstructionPointerNode node) => true;

    public bool VisitLoadOffsetParameterNode(LoadOffsetParameterNode node) => true;

    public bool VisitMethodGroupNode(MethodGroupNode node) => true;

    public bool VisitSegmentedAddressListNode(SegmentedAddressListNode node) => true;

    public bool VisitSignatureFieldNode(SignatureFieldNode node) => true;

    public bool VisitExplicitCastNode(ExplicitCastNode node) => true;

    private static bool Unlowered(IVisitableAstNode node) =>
        throw new InvalidOperationException($"{node.GetType().Name} must be lowered before completion analysis.");

    public bool VisitTupleSwapNode(TupleSwapNode node) => true;

    public bool VisitAluOperationNode(AluOperationNode node) => true;
    public bool VisitAluStatementNode(AluStatementNode node) => true;
    public bool VisitOpaqueNode(OpaqueNode node) => true;
    public bool VisitInstructionStartNode(InstructionStartNode node) => true;
    public bool VisitInterruptReturnNode(InterruptReturnNode node) => false;
    public bool VisitPreTestLoopNode(PreTestLoopNode node) => true;
    public bool VisitLocalDeclarationNode(LocalDeclarationNode node) => true;
}
