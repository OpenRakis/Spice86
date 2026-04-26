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
    /// <returns>A SegmentedAddressNode carrying both components.</returns>
    public SegmentedAddressNode ToNode(ValueNode segment, ValueNode offset) {
        return new SegmentedAddressNode(segment, offset);
    }

    public SegmentedAddressNode ToNode(InstructionField<SegmentedAddress> field) {
        if (field.UseValue) {
            return Constant.ToNode(field.Value);
        }

        ValueNode memoryAddress = Constant.ToNode(field.PhysicalAddress);
        return FromMemory(memoryAddress, BitWidth.WORD_16);
    }

    public SegmentedAddressNode FromMemory(ValueNode memoryAddress, BitWidth operandBitWidth) {
        ValueNode targetOffset;
        ValueNode segmentAddress;

        if (operandBitWidth == BitWidth.WORD_16) {
            targetOffset = new AbsolutePointerNode(DataType.UINT16, memoryAddress);
            segmentAddress = Constant.AddConstant(memoryAddress, 2);
        } else if (operandBitWidth == BitWidth.DWORD_32) {
            ValueNode targetOffset32 = new AbsolutePointerNode(DataType.UINT32, memoryAddress);
            targetOffset = TypeConversion.Convert(DataType.UINT16, targetOffset32);
            segmentAddress = Constant.AddConstant(memoryAddress, 4);
        } else {
            throw new ArgumentOutOfRangeException(nameof(operandBitWidth), operandBitWidth, "Operand bit width must be WORD_16 or DWORD_32.");
        }

        ValueNode targetSegment = new AbsolutePointerNode(DataType.UINT16, segmentAddress);
        return new SegmentedAddressNode(targetSegment, targetOffset);
    }
}