namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class InstructionFieldAstBuilder(ConstantAstBuilder constant, PointerAstBuilder pointer, SegmentedAddressAstBuilder segmentedAddress) {
    public ConstantAstBuilder Constant { get; } = constant;
    public PointerAstBuilder Pointer { get; } = pointer;
    public SegmentedAddressAstBuilder SegmentedAddress { get; } = segmentedAddress;

    public InstructionFieldNode ToNode(InstructionField<byte> field) {
        return new InstructionFieldNode(ToType(field), field, field.Value);
    }

    public InstructionFieldNode ToNode(InstructionField<ushort> field) {
        return new InstructionFieldNode(ToType(field), field, field.Value);
    }

    public InstructionFieldNode ToNode(InstructionField<uint> field) {
        return new InstructionFieldNode(ToType(field), field, field.Value);
    }

    public InstructionFieldNode ToNode(InstructionField<sbyte> field) {
        return new InstructionFieldNode(ToType(field), field, (ulong)(uint)field.Value);
    }

    public InstructionFieldNode ToNode(InstructionField<short> field) {
        return new InstructionFieldNode(ToType(field), field, (ulong)(uint)field.Value);
    }

    public InstructionFieldNode ToNode(InstructionField<int> field) {
        return new InstructionFieldNode(ToType(field), field, (ulong)(uint)field.Value);
    }

    public SegmentedAddressNode ToNode(InstructionField<SegmentedAddress> field) {
        return SegmentedAddress.ToNode(field);
    }

    public SegmentedAddressNode ToNode(InstructionField<SegmentedAddress32> field) {
        return SegmentedAddress.ToNode(field);
    }

    public DataType ToType(InstructionField<byte> field) {
        return DataType.UINT8;
    }

    public DataType ToType(InstructionField<ushort> field) {
        return DataType.UINT16;
    }

    public DataType ToType(InstructionField<uint> field) {
        return DataType.UINT32;
    }

    public DataType ToType(InstructionField<sbyte> field) {
        return DataType.INT8;
    }

    public DataType ToType(InstructionField<short> field) {
        return DataType.INT16;
    }

    public DataType ToType(InstructionField<int> field) {
        return DataType.INT32;
    }
}