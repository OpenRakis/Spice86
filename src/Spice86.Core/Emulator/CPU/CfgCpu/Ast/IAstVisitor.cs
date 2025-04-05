namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

public interface IAstVisitor<T> {
    public T VisitSegmentRegisterNode(SegmentRegisterNode node);
    public T VisitSegmentedPointer(SegmentedPointer node);
    public T VisitRegisterNode(RegisterNode node);
    public T VisitAbsolutePointerNode(AbsolutePointerNode node);
    public T VisitSegmentedAddressConstantNode(SegmentedAddressConstantNode node);
    public T VisitBinaryOperationNode(BinaryOperationNode node);
    public T VisitInstructionNode(InstructionNode node);
    public T VisitConstantNode(ConstantNode node);
}