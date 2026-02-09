namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public record AbsolutePointerNode(DataType DataType, ValueNode AbsoluteAddress) : ValueNode(DataType) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitAbsolutePointerNode(this);
    }
}