namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Encodes the inputs of a service call as numbers, the same way for a call through a handler's stub (read
/// from the registers) and for a direct call from generated code (computed from the call's arguments).
/// </summary>
public static class ServiceCallInputs {
    private const CpuValues DsDx = CpuValues.Ds | CpuValues.Dx;

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
        return binding switch {
            CpuValues.Al or CpuValues.Ah or CpuValues.Bl or CpuValues.Bh
                or CpuValues.Cl or CpuValues.Ch or CpuValues.Dl or CpuValues.Dh => Encoding.Byte,
            CpuValues.Ax or CpuValues.Bx or CpuValues.Cx or CpuValues.Dx
                or CpuValues.Sp or CpuValues.Bp or CpuValues.Si or CpuValues.Di
                or CpuValues.Es or CpuValues.Cs or CpuValues.Ss or CpuValues.Ds
                or CpuValues.Fs or CpuValues.Gs => Encoding.Word,
            CpuValues.Eax or CpuValues.Ebx or CpuValues.Ecx or CpuValues.Edx
                or CpuValues.Esp or CpuValues.Ebp or CpuValues.Esi or CpuValues.Edi => Encoding.DoubleWord,
            CpuValues.Carry or CpuValues.Parity or CpuValues.Auxiliary or CpuValues.Zero
                or CpuValues.Sign or CpuValues.Overflow or CpuValues.Direction => Encoding.Flag,
            DsDx => Encoding.SegmentedAddress,
            _ => throw new ArgumentOutOfRangeException(nameof(binding), binding,
                "A service input binding is a single register, a single flag or DS:DX.")
        };
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
        return binding switch {
            CpuValues.Al => state.AL,
            CpuValues.Ah => state.AH,
            CpuValues.Ax => state.AX,
            CpuValues.Eax => state.EAX,
            CpuValues.Bl => state.BL,
            CpuValues.Bh => state.BH,
            CpuValues.Bx => state.BX,
            CpuValues.Ebx => state.EBX,
            CpuValues.Cl => state.CL,
            CpuValues.Ch => state.CH,
            CpuValues.Cx => state.CX,
            CpuValues.Ecx => state.ECX,
            CpuValues.Dl => state.DL,
            CpuValues.Dh => state.DH,
            CpuValues.Dx => state.DX,
            CpuValues.Edx => state.EDX,
            CpuValues.Sp => state.SP,
            CpuValues.Esp => state.ESP,
            CpuValues.Bp => state.BP,
            CpuValues.Ebp => state.EBP,
            CpuValues.Si => state.SI,
            CpuValues.Esi => state.ESI,
            CpuValues.Di => state.DI,
            CpuValues.Edi => state.EDI,
            CpuValues.Es => state.ES,
            CpuValues.Cs => state.CS,
            CpuValues.Ss => state.SS,
            CpuValues.Ds => state.DS,
            CpuValues.Fs => state.FS,
            CpuValues.Gs => state.GS,
            CpuValues.Carry => FlagValue(state.CarryFlag),
            CpuValues.Parity => FlagValue(state.ParityFlag),
            CpuValues.Auxiliary => FlagValue(state.AuxiliaryFlag),
            CpuValues.Zero => FlagValue(state.ZeroFlag),
            CpuValues.Sign => FlagValue(state.SignFlag),
            CpuValues.Overflow => FlagValue(state.OverflowFlag),
            CpuValues.Direction => FlagValue(state.DirectionFlag),
            DsDx => ((uint)state.DS << 16) | state.DX,
            _ => throw new ArgumentOutOfRangeException(nameof(binding), binding,
                "A service input binding is a single register, a single flag or DS:DX.")
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

    private static uint FlagValue(bool flag) => flag ? 1u : 0u;
}
