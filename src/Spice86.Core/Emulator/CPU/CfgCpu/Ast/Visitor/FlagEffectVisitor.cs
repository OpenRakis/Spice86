namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Computes the status flags an instruction AST reads before writing them, always writes and may write.
/// Code the analysis does not know (unknown method calls, interrupts, callbacks, unrecognised
/// instructions) reads all six status flags and may write all six. The visitor describes what the
/// instruction itself does; where flags stay live across a control transfer is decided separately.
/// </summary>
internal sealed class FlagEffectVisitor : IAstVisitor<FlagEffect> {
    private static readonly FlagEffectVisitor Instance = new();

    /// <summary>The status-flag effect of the instruction AST <paramref name="node"/>.</summary>
    public static FlagEffect Of(IVisitableAstNode node) => node.Accept(Instance);

    private FlagEffectVisitor() {
    }

    public FlagEffect VisitBlockNode(BlockNode node) {
        FlagEffect effect = FlagEffect.None;
        foreach (IVisitableAstNode statement in node.Statements) {
            effect = effect.Then(statement.Accept(this));
        }
        return effect;
    }

    public FlagEffect VisitIfElseNode(IfElseNode node) {
        FlagEffect arms = FlagEffect.Either(node.TrueCase.Accept(this), node.FalseCase.Accept(this));
        return node.Condition.Accept(this).Then(arms);
    }

    public FlagEffect VisitWhileNode(WhileNode node) {
        FlagEffect condition = node.Condition.Accept(this);
        FlagEffect afterBody = node.Body.Accept(this).Then(condition);
        return condition.Then(FlagEffect.Either(FlagEffect.None, afterBody));
    }

    public FlagEffect VisitWhileWithExitNode(WhileWithExitNode node) {
        FlagEffect condition = node.Condition.Accept(this);
        FlagEffect iteration = node.Body.Accept(this).Then(node.ExitCondition.Accept(this)).Then(condition);
        return condition.Then(FlagEffect.Either(FlagEffect.None, iteration));
    }

    public FlagEffect VisitBinaryOperationNode(BinaryOperationNode node) {
        if (node.BinaryOperation == BinaryOperation.ASSIGN && node.Left is CpuFlagNode flag) {
            return node.Right.Accept(this).Then(FlagEffect.Writing(StatusBit(flag)));
        }
        if (node.BinaryOperation == BinaryOperation.ASSIGN && node.Left is FlagRegisterNode) {
            return node.Right.Accept(this).Then(FlagEffect.Writing(StatusFlags.All));
        }
        return node.Left.Accept(this).Then(node.Right.Accept(this));
    }

    public FlagEffect VisitUnaryOperationNode(UnaryOperationNode node) => node.Value.Accept(this);

    public FlagEffect VisitConditionalValueNode(ConditionalValueNode node) =>
        node.Condition.Accept(this)
            .Then(FlagEffect.Either(node.WhenTrue.Accept(this), node.WhenFalse.Accept(this)));

    public FlagEffect VisitTypeConversionNode(TypeConversionNode node) => node.Value.Accept(this);

    public FlagEffect VisitCpuFlagNode(CpuFlagNode node) => FlagEffect.Reading(StatusBit(node));

    public FlagEffect VisitFlagRegisterNode(FlagRegisterNode node) => FlagEffect.Reading(StatusFlags.All);

    public FlagEffect VisitMethodCallValueNode(MethodCallValueNode node) {
        if (node is PortReadCallNode or BitScanCallNode) {
            FlagEffect effect = FlagEffect.None;
            foreach (IVisitableAstNode argument in node.CallNode.Arguments) {
                effect = effect.Then(argument.Accept(this));
            }
            return effect;
        }
        return node.CallNode.Accept(this);
    }

    public FlagEffect VisitMethodCallNode(MethodCallNode node) {
        FlagEffect effect = FlagEffect.None;
        foreach (IVisitableAstNode argument in node.Arguments) {
            effect = effect.Then(argument.Accept(this));
        }
        return effect.Then(CallEffect(node));
    }

    public FlagEffect VisitSetInterruptShadowNode(SetInterruptShadowNode node) => FlagEffect.None;

    public FlagEffect VisitVariableDeclarationNode(VariableDeclarationNode node) => node.Initializer.Accept(this);

    public FlagEffect VisitInstructionFieldNode(InstructionFieldNode node) => node.ResolvedNode.Accept(this);

    public FlagEffect VisitSegmentedPointer(SegmentedPointerNode node) {
        FlagEffect effect = node.Segment.Accept(this);
        if (node.DefaultSegment != null) {
            effect = effect.Then(node.DefaultSegment.Accept(this));
        }
        return effect.Then(node.Offset.Accept(this));
    }

    public FlagEffect VisitAbsolutePointerNode(AbsolutePointerNode node) => node.AbsoluteAddress.Accept(this);

    public FlagEffect VisitSegmentedAddressNode(SegmentedAddressNode node) =>
        node.Segment.Accept(this).Then(node.Offset.Accept(this));

    public FlagEffect VisitJumpNearNode(JumpNearNode node) => node.Ip.Accept(this);

    public FlagEffect VisitCallNearNode(CallNearNode node) => node.TargetIp.Accept(this);

    public FlagEffect VisitJumpFarNode(JumpFarNode node) => node.TargetAddress.Accept(this);

    public FlagEffect VisitCallFarNode(CallFarNode node) => node.TargetAddress.Accept(this);

    public FlagEffect VisitReturnNearNode(ReturnNearNode node) => node.BytesToPop.Accept(this);

    public FlagEffect VisitReturnFarNode(ReturnFarNode node) => node.BytesToPop.Accept(this);

    public FlagEffect VisitInterruptCallNode(InterruptCallNode node) =>
        node.VectorNumber.Accept(this).Then(FlagEffect.Unknown);

    public FlagEffect VisitCallbackNode(CallbackNode node) => node.CallbackNumber.Accept(this).Then(FlagEffect.Unknown);

    public FlagEffect VisitReturnInterruptNode(ReturnInterruptNode node) => FlagEffect.Writing(StatusFlags.All);

    public FlagEffect VisitInstructionNode(InstructionNode node) => FlagEffect.Unknown;

    public FlagEffect VisitRegisterNode(RegisterNode node) => FlagEffect.None;

    public FlagEffect VisitSegmentRegisterNode(SegmentRegisterNode node) => FlagEffect.None;

    public FlagEffect VisitConstantNode(ConstantNode node) => FlagEffect.None;

    public FlagEffect VisitNearAddressNode(NearAddressNode node) => FlagEffect.None;

    public FlagEffect VisitVariableReferenceNode(VariableReferenceNode node) => FlagEffect.None;

    public FlagEffect VisitThrowNode(ThrowNode node) => FlagEffect.None;

    public FlagEffect VisitMoveIpNextNode(MoveIpNextNode node) => FlagEffect.None;

    public FlagEffect VisitHltNode(HltNode node) => FlagEffect.None;

    public FlagEffect VisitSelectorNode(SelectorNode node) => FlagEffect.None;

    public FlagEffect VisitInvalidInstructionNode(InvalidInstructionNode node) => FlagEffect.None;

    public FlagEffect VisitCpuidNode(CpuidNode node) => FlagEffect.None;

    private static FlagEffect CallEffect(MethodCallNode node) {
        if (node is AluCallNode alu) {
            return AluEffect(alu);
        }
        if (node is StackCallNode or PortWriteCallNode or BoundCheckCallNode) {
            return FlagEffect.None;
        }
        return FlagEffect.Unknown;
    }

    private static FlagEffect AluEffect(AluCallNode node) {
        ulong? count = null;
        if (node.Arguments.Count > 0) {
            count = ConstantValue(node.Arguments[^1]);
        }
        if (AluFlagTable.TryGetEffect(node.Operation, node.Width, count, out FlagEffect effect)) {
            return effect;
        }
        return FlagEffect.Unknown;
    }

    private static ulong? ConstantValue(IVisitableAstNode node) {
        if (node is ConstantNode constant) {
            return constant.Value;
        }
        if (node is InstructionFieldNode { ResolvedNode: ConstantNode resolved }) {
            return resolved.Value;
        }
        return null;
    }

    private static StatusFlags StatusBit(CpuFlagNode node) => (StatusFlags)node.FlagMask & StatusFlags.All;
}
