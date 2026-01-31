namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

public interface IAstVisitor<T> {
    public T VisitSegmentRegisterNode(SegmentRegisterNode node);
    public T VisitSegmentedPointer(SegmentedPointerNode node);
    public T VisitRegisterNode(RegisterNode node);
    public T VisitAbsolutePointerNode(AbsolutePointerNode node);
    public T VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node);
    public T VisitBinaryOperationNode(BinaryOperationNode node);
    public T VisitUnaryOperationNode(UnaryOperationNode node);
    public T VisitTypeConversionNode(TypeConversionNode node);
    public T VisitInstructionNode(InstructionNode node);
    public T VisitConstantNode(ConstantNode node);
    public T VisitMethodCallNode(MethodCallNode node);
    public T VisitMethodCallValueNode(MethodCallValueNode node);
    public T VisitBlockNode(BlockNode node);
    public T VisitIfElseNode(IfElseNode node);
    public T VisitCpuFlagNode(CpuFlagNode node);
    public T VisitVariableReferenceNode(VariableReferenceNode node);
    public T VisitVariableDeclarationNode(VariableDeclarationNode node);

    // Control Flow
    public T VisitMoveIpNextNode(MoveIpNextNode node);
    public T VisitCallNearNode(CallNearNode node);
    public T VisitCallFarNode(CallFarNode node);
    public T VisitReturnNearNode(ReturnNearNode node);
    public T VisitReturnFarNode(ReturnFarNode node);
    public T VisitJumpNearNode(JumpNearNode node);
    public T VisitJumpFarNode(JumpFarNode node);
    public T VisitInterruptCallNode(InterruptCallNode node);
    public T VisitReturnInterruptNode(ReturnInterruptNode node);
}