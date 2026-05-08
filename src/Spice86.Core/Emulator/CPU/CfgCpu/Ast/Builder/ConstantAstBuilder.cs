namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Shared.Emulator.Memory;

public class ConstantAstBuilder {
    public NearAddressNode ToNearAddressNode(ushort value, SegmentedAddress baseAddress) {
        return new NearAddressNode(value, baseAddress);
    }

    public ValueNode ToNode(byte value) {
        return new ConstantNode(DataType.UINT8, value);
    }

    public ValueNode ToNode(ushort value) {
        return new ConstantNode(DataType.UINT16, value);
    }

    public ValueNode ToNode(uint value) {
        return new ConstantNode(DataType.UINT32, value);
    }
    
    public ValueNode ToNode(ulong value) {
        return new ConstantNode(DataType.UINT64, value);
    }
    
    public ValueNode ToNode(bool value) {
        return new ConstantNode(DataType.BOOL, value ? 1ul : 0ul);
    }

    public ValueNode ToNode(DataType dataType, ulong value) {
        return new ConstantNode(dataType, value);
    }

    /// <summary>
    /// Adds a signed constant delta to a value node.
    /// Positive deltas produce PLUS operations; negative deltas produce MINUS operations with absolute value.
    /// </summary>
    /// <param name="value">Value being adjusted.</param>
    /// <param name="constantDelta">Signed constant delta.</param>
    /// <returns>The adjusted expression, or the original value when delta is zero.</returns>
    public ValueNode AddConstant(ValueNode value, long constantDelta) {
        return AddConstant(value.DataType, value, constantDelta);
    }

    /// <summary>
    /// Adds a signed constant delta to a value node using an explicit expression data type.
    /// Positive deltas produce PLUS operations; negative deltas produce MINUS operations with absolute value.
    /// </summary>
    /// <param name="dataType">Expression data type.</param>
    /// <param name="value">Value being adjusted.</param>
    /// <param name="constantDelta">Signed constant delta.</param>
    /// <returns>The adjusted expression, or the original value when delta is zero.</returns>
    public ValueNode AddConstant(DataType dataType, ValueNode value, long constantDelta) {
        if (constantDelta == 0) {
            return value;
        }

        if (constantDelta > 0) {
            ValueNode deltaNode = ToNode(dataType, (ulong)constantDelta);
            return new BinaryOperationNode(dataType, value, BinaryOperation.PLUS, deltaNode);
        }

        if (constantDelta == long.MinValue) {
            throw new ArgumentOutOfRangeException(nameof(constantDelta), constantDelta,
                "Absolute value cannot be represented for long.MinValue.");
        }

        ulong absoluteDelta = (ulong)Math.Abs(constantDelta);
        ValueNode absoluteDeltaNode = ToNode(dataType, absoluteDelta);
        return new BinaryOperationNode(dataType, value, BinaryOperation.MINUS, absoluteDeltaNode);
    }

    public ValueNode ToNode(sbyte value) {
        return new ConstantNode(DataType.INT8, (byte)value);
    }

    public ValueNode ToNode(short value) {
        return new ConstantNode(DataType.INT16, (ushort)value);
    }

    public ValueNode ToNode(int value) {
        return new ConstantNode(DataType.INT32, (uint)value);
    }
    
    public ValueNode ToNode(long value) {
        return new ConstantNode(DataType.INT64, (ulong)value);
    }

    public SegmentedAddressNode ToNode(SegmentedAddress segmentedAddress) {
        ValueNode segment = ToNode(segmentedAddress.Segment);
        ValueNode offset = ToNode(segmentedAddress.Offset);
        return new SegmentedAddressNode(segment, offset);
    }

    public SegmentedAddressNode ToNode(SegmentedAddress32 segmentedAddress) {
        ValueNode segment = ToNode(segmentedAddress.Segment);
        ValueNode offset = ToNode(segmentedAddress.Offset);
        return new SegmentedAddressNode(segment, offset);
    }
}