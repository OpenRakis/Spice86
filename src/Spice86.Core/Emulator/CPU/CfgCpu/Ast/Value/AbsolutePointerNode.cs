namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;

public class AbsolutePointerNode(DataType dataType, ValueNode absoluteAddress) : ValueNode(dataType) {
    public ValueNode AbsoluteAddress { get; } = absoluteAddress;
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitAbsolutePointerNode(this);
    }
}