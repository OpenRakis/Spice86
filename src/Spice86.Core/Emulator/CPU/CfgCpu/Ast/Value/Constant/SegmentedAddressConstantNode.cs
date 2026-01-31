namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

public record SegmentedAddressConstantNode(SegmentedAddress Value) : ValueNode(DataType.UINT32) {
    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSegmentedAddressConstantNode(this);
    }
}