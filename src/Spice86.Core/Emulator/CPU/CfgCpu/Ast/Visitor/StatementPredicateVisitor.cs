namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

/// <summary>
/// Base for boolean queries over an execution AST that look for a statement with a given effect.
/// Statement containers (<see cref="BlockNode"/>, <see cref="IfElseNode"/>, <see cref="WhileNode"/>) are
/// searched recursively; <see cref="VisitBinaryOperationNode"/> decides whether an assignment has the effect;
/// every other node returns false. A node type added to <see cref="IAstVisitor{T}"/> must be decided here
/// for all predicates at once.
/// </summary>
internal abstract class StatementPredicateVisitor : IAstVisitor<bool> {
    public abstract bool VisitBinaryOperationNode(BinaryOperationNode node);

    public bool VisitBlockNode(BlockNode node) {
        foreach (IVisitableAstNode statement in node.Statements) {
            if (statement.Accept(this)) {
                return true;
            }
        }
        return false;
    }

    public bool VisitIfElseNode(IfElseNode node) {
        return node.TrueCase.Accept(this) || node.FalseCase.Accept(this);
    }

    public bool VisitWhileNode(WhileNode node) {
        return node.Body.Accept(this);
    }

    // Every other node returns false; override one in a predicate whose effect it can carry.
    public virtual bool VisitInstructionFieldNode(InstructionFieldNode node) => false;
    public virtual bool VisitSegmentRegisterNode(SegmentRegisterNode node) => false;
    public virtual bool VisitSegmentedPointer(SegmentedPointerNode node) => false;
    public virtual bool VisitRegisterNode(RegisterNode node) => false;
    public virtual bool VisitAbsolutePointerNode(AbsolutePointerNode node) => false;
    public virtual bool VisitSegmentedAddressNode(SegmentedAddressNode node) => false;
    public virtual bool VisitUnaryOperationNode(UnaryOperationNode node) => false;
    public virtual bool VisitTypeConversionNode(TypeConversionNode node) => false;
    public virtual bool VisitConstantNode(ConstantNode node) => false;
    public virtual bool VisitNearAddressNode(NearAddressNode node) => false;
    public virtual bool VisitCpuFlagNode(CpuFlagNode node) => false;
    public virtual bool VisitFlagRegisterNode(FlagRegisterNode node) => false;
    public virtual bool VisitVariableReferenceNode(VariableReferenceNode node) => false;
    public virtual bool VisitVariableDeclarationNode(VariableDeclarationNode node) => false;
    public virtual bool VisitThrowNode(ThrowNode node) => false;
    public virtual bool VisitCpuidNode(CpuidNode node) => false;
    public virtual bool VisitInstructionNode(InstructionNode node) => false;
    public virtual bool VisitMethodCallNode(MethodCallNode node) => false;
    public virtual bool VisitMethodCallValueNode(MethodCallValueNode node) => false;
    public virtual bool VisitMoveIpNextNode(MoveIpNextNode node) => false;
    public virtual bool VisitCallNearNode(CallNearNode node) => false;
    public virtual bool VisitCallFarNode(CallFarNode node) => false;
    public virtual bool VisitReturnNearNode(ReturnNearNode node) => false;
    public virtual bool VisitReturnFarNode(ReturnFarNode node) => false;
    public virtual bool VisitJumpNearNode(JumpNearNode node) => false;
    public virtual bool VisitJumpFarNode(JumpFarNode node) => false;
    public virtual bool VisitHltNode(HltNode node) => false;
    public virtual bool VisitInterruptCallNode(InterruptCallNode node) => false;
    public virtual bool VisitReturnInterruptNode(ReturnInterruptNode node) => false;
    public virtual bool VisitCallbackNode(CallbackNode node) => false;
    public virtual bool VisitSelectorNode(SelectorNode node) => false;
    public virtual bool VisitInvalidInstructionNode(InvalidInstructionNode node) => false;
}
