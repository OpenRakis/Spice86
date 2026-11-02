namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>Conversions between <see cref="IrValues"/> and the register / status-flag sets it packs.</summary>
internal static class IrValueSets {
    /// <summary>
    /// The <see cref="IrValues"/> bytes <paramref name="register"/> covers: one byte for an 8-bit
    /// register (the x86 index picks the low or high byte of its 16-bit register), two for a 16-bit
    /// register, four for a 32-bit one, its own bit for a segment register.
    /// </summary>
    public static IrValues Of(RegisterNode register) {
        if (register is SegmentRegisterNode) {
            return (IrValues)(1UL << (32 + register.RegisterIndex));
        }
        int index = register.RegisterIndex;
        return register.DataType.BitWidth switch {
            BitWidth.BYTE_8 => (IrValues)ByteOf8Bit(index),
            BitWidth.WORD_16 => (IrValues)(0x3UL << (4 * index)),
            _ => (IrValues)(0xFUL << (4 * index))
        };
    }

    /// <summary>The <see cref="IrValues"/> of a status-flag set.</summary>
    public static IrValues FromStatusFlags(StatusFlags flags) {
        IrValues result = IrValues.None;
        foreach ((StatusFlags flag, IrValues value) in StatusFlagValues) {
            if ((flags & flag) != 0) {
                result |= value;
            }
        }
        return result;
    }

    private static readonly (StatusFlags Flag, IrValues Value)[] StatusFlagValues = [
        (StatusFlags.Carry, IrValues.Carry), (StatusFlags.Parity, IrValues.Parity),
        (StatusFlags.Auxiliary, IrValues.Auxiliary), (StatusFlags.Zero, IrValues.Zero),
        (StatusFlags.Sign, IrValues.Sign), (StatusFlags.Overflow, IrValues.Overflow)];

    private static ulong ByteOf8Bit(int index) {
        uint encoded = (uint)index;
        int register = (int)UInt8HighLowRegistersIndexer.ComputeRegisterIndexInArray(encoded);
        int byteIndex = UInt8HighLowRegistersIndexer.IsHigh(encoded) ? 1 : 0;
        return 1UL << (4 * register + byteIndex);
    }
}
