namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory.Indexable;

public class InstructionFieldValueRetriever {
    private IIndexable Memory { get; }

    public InstructionFieldValueRetriever(IIndexable memory) {
        Memory = memory;
    }

    public byte GetFieldValue(InstructionField<byte> field) {
        if (field.UseValue) {
            return field.Value;
        }

        return Memory.UInt8[field.PhysicalAddress];
    }

    public ushort GetFieldValue(InstructionField<ushort> field) {
        if (field.UseValue) {
            return field.Value;
        }

        return Memory.UInt16[field.PhysicalAddress];
    }

    public uint GetFieldValue(InstructionField<uint> field) {
        if (field.UseValue) {
            return field.Value;
        }

        return Memory.UInt32[field.PhysicalAddress];
    }

    public sbyte GetFieldValue(InstructionField<sbyte> field) {
        if (field.UseValue) {
            return field.Value;
        }

        return Memory.Int8[field.PhysicalAddress];
    }

    public short GetFieldValue(InstructionField<short> field) {
        if (field.UseValue) {
            return field.Value;
        }

        return Memory.Int16[field.PhysicalAddress];
    }

    public int GetFieldValue(InstructionField<int> field) {
        if (field.UseValue) {
            return field.Value;
        }

        return Memory.Int32[field.PhysicalAddress];
    }
}