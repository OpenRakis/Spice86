namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Shared.Emulator.Memory;

public class SegmentedAddressConstantNode(SegmentedAddress value) : ValueNode(DataType.UINT32) {
    public SegmentedAddress Value { get; } = value;

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSegmentedAddressConstantNode(this);
    }
}