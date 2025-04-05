namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

public class ConstantNode(DataType dataType, uint value) : ValueNode(dataType) {
    public uint Value { get; } = value;
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitConstantNode(this);
    }
}