namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using Spice86.Shared.Emulator.Memory;


public record NearAddressNode : ConstantNode {
    public NearAddressNode(ushort value, SegmentedAddress baseAddress) : base(DataType.UINT16, value) {
        BaseAddress = baseAddress;
    }

    /// <summary>
    /// Base address from which delta can be computed
    /// </summary>
    public SegmentedAddress BaseAddress { get; init; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitNearAddressNode(this);
    }
}