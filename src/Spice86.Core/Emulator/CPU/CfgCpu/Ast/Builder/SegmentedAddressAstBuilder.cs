namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class SegmentedAddressAstBuilder(ConstantAstBuilder constant, TypeConversionAstBuilder typeConversion) {
    public ConstantAstBuilder Constant { get; } = constant;
    public TypeConversionAstBuilder TypeConversion { get; } = typeConversion;

    /// <summary>
    /// Creates a segmented address value node from segment and offset expressions.
    /// </summary>
    /// <param name="segment">Segment expression.</param>
    /// <param name="offset">Offset expression.</param>
    /// <returns>A SegmentedAddressValueNode carrying both components.</returns>
    public SegmentedAddressValueNode ToNode(ValueNode segment, ValueNode offset) {
        return new SegmentedAddressValueNode(segment, offset);
    }

    public SegmentedAddressValueNode ToNode(InstructionField<SegmentedAddress> field) {
        if (field.UseValue) {
            return Constant.ToNode(field.Value);
        }

        ValueNode memoryAddress = Constant.ToNode(field.PhysicalAddress);
        return FromMemory(memoryAddress, 16);
    }

    public SegmentedAddressValueNode FromMemory(ValueNode memoryAddress, int operandSize) {
        ValueNode targetOffset;
        ValueNode segmentAddress;

        if (operandSize == 16) {
            targetOffset = new AbsolutePointerNode(DataType.UINT16, memoryAddress);
            segmentAddress = Constant.AddConstant(memoryAddress, 2);
        } else if (operandSize == 32) {
            ValueNode targetOffset32 = new AbsolutePointerNode(DataType.UINT32, memoryAddress);
            targetOffset = TypeConversion.Convert(DataType.UINT16, targetOffset32);
            segmentAddress = Constant.AddConstant(memoryAddress, 4);
        } else {
            throw new ArgumentOutOfRangeException(nameof(operandSize), operandSize, "Operand size must be 16 or 32.");
        }

        ValueNode targetSegment = new AbsolutePointerNode(DataType.UINT16, segmentAddress);
        return new SegmentedAddressValueNode(targetSegment, targetOffset);
    }
}