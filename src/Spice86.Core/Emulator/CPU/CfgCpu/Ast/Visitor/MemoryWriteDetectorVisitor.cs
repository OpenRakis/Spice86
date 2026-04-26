namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

/// <summary>
/// Visitor that detects whether an AST contains a memory write (an assignment to
/// an <see cref="AbsolutePointerNode"/> or <see cref="SegmentedPointerNode"/>).
/// Implements <see cref="IAstVisitor{T}"/> so that adding a new node type to the
/// interface forces a compilation error here, preventing silent omissions.
/// </summary>
internal sealed class MemoryWriteDetectorVisitor : IAstVisitor<bool> {
    private static readonly MemoryWriteDetectorVisitor Instance = new();

    /// <summary>
    /// Returns whether the given AST tree contains a memory write.
    /// </summary>
    public static bool ContainsMemoryWrite(IVisitableAstNode node) {
        return node.Accept(Instance);
    }

    public bool VisitBinaryOperationNode(BinaryOperationNode node) {
        return node.BinaryOperation is BinaryOperation.ASSIGN
               && node.Left is AbsolutePointerNode or SegmentedPointerNode;
    }

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

    // Leaf / non-container nodes — cannot contain memory writes.

    public bool VisitInstructionFieldNode(InstructionFieldNode node) => false;
    public bool VisitSegmentRegisterNode(SegmentRegisterNode node) => false;
    public bool VisitSegmentedPointer(SegmentedPointerNode node) => false;
    public bool VisitRegisterNode(RegisterNode node) => false;
    public bool VisitAbsolutePointerNode(AbsolutePointerNode node) => false;
    public bool VisitSegmentedAddressNode(SegmentedAddressNode node) => false;
    public bool VisitUnaryOperationNode(UnaryOperationNode node) => false;
    public bool VisitTypeConversionNode(TypeConversionNode node) => false;
    public bool VisitConstantNode(ConstantNode node) => false;
    public bool VisitNearAddressNode(NearAddressNode node) => false;
    public bool VisitCpuFlagNode(CpuFlagNode node) => false;
    public bool VisitFlagRegisterNode(FlagRegisterNode node) => false;
    public bool VisitVariableReferenceNode(VariableReferenceNode node) => false;
    public bool VisitThrowNode(ThrowNode node) => false;
    public bool VisitCpuidNode(CpuidNode node) => false;

    // Nodes with children that are not structural containers for statements.
    // InstructionNode, MethodCallNode, etc. hold operands, not statement bodies.
    // They don't wrap memory-write assignments.

    public bool VisitInstructionNode(InstructionNode node) => false;
    public bool VisitMethodCallNode(MethodCallNode node) => false;
    public bool VisitMethodCallValueNode(MethodCallValueNode node) => false;
    public bool VisitVariableDeclarationNode(VariableDeclarationNode node) => false;

    // Control flow nodes — these change IP/execution flow, not memory.

    public bool VisitMoveIpNextNode(MoveIpNextNode node) => false;
    public bool VisitCallNearNode(CallNearNode node) => false;
    public bool VisitCallFarNode(CallFarNode node) => false;
    public bool VisitReturnNearNode(ReturnNearNode node) => false;
    public bool VisitReturnFarNode(ReturnFarNode node) => false;
    public bool VisitJumpNearNode(JumpNearNode node) => false;
    public bool VisitJumpFarNode(JumpFarNode node) => false;
    public bool VisitHltNode(HltNode node) => false;
    public bool VisitInterruptCallNode(InterruptCallNode node) => false;
    public bool VisitReturnInterruptNode(ReturnInterruptNode node) => false;
    public bool VisitCallbackNode(CallbackNode node) => false;
    public bool VisitSelectorNode(SelectorNode node) => false;
    public bool VisitInvalidInstructionNode(InvalidInstructionNode node) => false;
}
