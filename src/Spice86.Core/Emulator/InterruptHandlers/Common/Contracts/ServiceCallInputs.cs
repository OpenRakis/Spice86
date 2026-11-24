namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using Spice86.Core.Emulator.CPU;

using System.Numerics;

/// <summary>
/// Encodes the inputs of a service call as numbers, the same way for a call through a handler's stub (read
/// from the registers) and for a direct call from generated code (computed from the call's arguments).
/// The register or flag a binding names is found from the bit layout of <see cref="CpuValues"/>, so a register
/// that fits that layout needs no change here.
/// </summary>
public static class ServiceCallInputs {
    private const CpuValues DsDx = CpuValues.Ds | CpuValues.Dx;
    private const int BitsPerGeneralRegister = 4;
    private const int FirstSegmentRegisterBit = 32;
    private const int FirstFlagBit = 38;
    private const ulong LowByteBits = 0x1;
    private const ulong HighByteBits = 0x2;
    private const ulong WordBits = 0x3;
    private const ulong DoubleWordBits = 0xF;

    /// <summary>The <see cref="Flags"/> mask of each flag bit of <see cref="CpuValues"/>, from
    /// <see cref="CpuValues.Carry"/> to <see cref="CpuValues.Direction"/>.</summary>
    private static readonly ushort[] FlagMasks = [
        Flags.Carry, Flags.Parity, Flags.Auxiliary, Flags.Zero, Flags.Sign, Flags.Overflow, Flags.Direction];

    /// <summary>
    /// How the value of an input binding is encoded.
    /// </summary>
    public enum Encoding {
        /// <summary>An 8-bit register, zero-extended.</summary>
        Byte,

        /// <summary>A 16-bit register or a segment register, zero-extended.</summary>
        Word,

        /// <summary>A 32-bit register.</summary>
        DoubleWord,

        /// <summary>A flag: 1 when set, 0 when clear.</summary>
        Flag,

        /// <summary>A DS:DX address: DS in the upper 16 bits, DX in the lower 16.</summary>
        SegmentedAddress
    }

    /// <summary>
    /// Tells how the value of <paramref name="binding"/> is encoded.
    /// </summary>
    /// <param name="binding">An input binding of a service entry.</param>
    /// <returns>The encoding.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="binding"/> is not a single register, a
    /// single flag or DS:DX.</exception>
    public static Encoding EncodingOf(CpuValues binding) {
        if (binding == DsDx) {
            return Encoding.SegmentedAddress;
        }
        int bit = LowestBit(binding);
        if (bit < FirstSegmentRegisterBit) {
            return GeneralRegisterEncoding(binding, bit);
        }
        if (!BitOperations.IsPow2((ulong)binding) || bit >= FirstFlagBit + FlagMasks.Length) {
            throw NotAnInputBinding(binding);
        }
        return bit < FirstFlagBit ? Encoding.Word : Encoding.Flag;
    }

    /// <summary>
    /// Reads the value of <paramref name="binding"/> from the CPU state, encoded as
    /// <see cref="EncodingOf"/> tells.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="binding">An input binding of a service entry.</param>
    /// <returns>The encoded value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="binding"/> is not a single register, a
    /// single flag or DS:DX.</exception>
    public static uint Read(State state, CpuValues binding) {
        Encoding encoding = EncodingOf(binding);
        int bit = LowestBit(binding);
        uint register = (uint)(bit / BitsPerGeneralRegister);
        return encoding switch {
            Encoding.SegmentedAddress => ((uint)state.DS << 16) | state.DX,
            Encoding.Flag => state.Flags.GetFlag(FlagMasks[bit - FirstFlagBit]) ? 1u : 0u,
            Encoding.Word when bit >= FirstSegmentRegisterBit =>
                state.SegmentRegisters.UInt16[(uint)(bit - FirstSegmentRegisterBit)],
            Encoding.Word => state.GeneralRegisters.UInt16[register],
            Encoding.DoubleWord => state.GeneralRegisters.UInt32[register],
            _ when bit % BitsPerGeneralRegister == 0 => state.GeneralRegisters.UInt8Low[register],
            _ => state.GeneralRegisters.UInt8High[register]
        };
    }

    /// <summary>
    /// Reads the value of every input <paramref name="entry"/> binds to its implementation.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="entry">The service entry.</param>
    /// <returns>One value per binding, in the entry's order. Empty when the entry binds no implementation.
    /// </returns>
    public static IReadOnlyList<ServiceCallInput> ReadAll(State state, InterruptServiceEntry entry) {
        if (entry.Implementation is null || entry.Inputs.Count == 0) {
            return [];
        }
        ServiceCallInput[] inputs = new ServiceCallInput[entry.Inputs.Count];
        for (int i = 0; i < inputs.Length; i++) {
            CpuValues binding = entry.Inputs[i];
            inputs[i] = new ServiceCallInput(binding, Read(state, binding));
        }
        return inputs;
    }

    private static int LowestBit(CpuValues binding) => BitOperations.TrailingZeroCount((ulong)binding);

    private static Encoding GeneralRegisterEncoding(CpuValues binding, int bit) {
        int shift = bit / BitsPerGeneralRegister * BitsPerGeneralRegister;
        return ((ulong)binding >> shift) switch {
            LowByteBits or HighByteBits => Encoding.Byte,
            WordBits => Encoding.Word,
            DoubleWordBits => Encoding.DoubleWord,
            _ => throw NotAnInputBinding(binding)
        };
    }

    private static ArgumentOutOfRangeException NotAnInputBinding(CpuValues binding) {
        return new ArgumentOutOfRangeException(nameof(binding), binding,
            "A service input binding is a single register, a single flag or DS:DX.");
    }
}
