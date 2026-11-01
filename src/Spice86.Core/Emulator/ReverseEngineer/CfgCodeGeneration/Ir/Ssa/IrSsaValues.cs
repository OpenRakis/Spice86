namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

/// <summary>
/// Conversions between <see cref="IrEffects"/> register-byte / flag / token sets and
/// <see cref="IrSsaValue"/> bit sets. A general register's SSA value covers all four of its
/// <see cref="CpuValues"/> bits: writing all of them is a full definition, writing fewer or may-writing
/// is a partial definition.
/// </summary>
internal static class IrSsaValues {
    /// <summary>Bit set over <see cref="IrSsaValue"/> with every bit set.</summary>
    public const uint All = (1u << (int)IrSsaValue.Count) - 1;

    /// <summary>The low-word <see cref="CpuValues"/> bits of each 32-bit register, in
    /// <see cref="IrSsaValue"/> order.</summary>
    private static readonly CpuValues[] LowWordMasks = IrValueSets.RegisterLowWordMasks;

    /// <summary>The <see cref="CpuValues"/> bits making up the low word of a general register
    /// <paramref name="value"/>; only meaningful for values up to <see cref="IrSsaValue.Edi"/>.</summary>
    public static CpuValues LowWordMaskOf(IrSsaValue value) => LowWordMasks[(int)value];

    /// <summary>The <see cref="CpuValues"/> bits making up <paramref name="value"/>; only meaningful for values below <see cref="IrSsaValue.Memory"/>.</summary>
    public static CpuValues MaskOf(IrSsaValue value) =>
        value <= IrSsaValue.Direction ? IrValueSets.Values[(int)value].Mask : CpuValues.None;

    /// <summary>The SSA values <paramref name="values"/> touches, as a bit set indexed by <see cref="IrSsaValue"/>.</summary>
    public static uint ValueBits(CpuValues values) {
        uint result = 0;
        for (int v = 0; v <= (int)IrSsaValue.Direction; v++) {
            if ((values & MaskOf((IrSsaValue)v)) != CpuValues.None) {
                result |= 1u << v;
            }
        }
        return result;
    }

    /// <summary>The SSA values <paramref name="tokens"/> touches, as a bit set indexed by <see cref="IrSsaValue"/>.</summary>
    public static uint TokenBits(IrTokens tokens) {
        uint result = 0;
        if (tokens.HasFlag(IrTokens.Memory)) {
            result |= 1u << (int)IrSsaValue.Memory;
        }
        if (tokens.HasFlag(IrTokens.Io)) {
            result |= 1u << (int)IrSsaValue.Io;
        }
        return result;
    }

    /// <summary>
    /// Splits the values <paramref name="effects"/> writes into the fully defined ones (all the value's
    /// bits in <see cref="IrEffects.MustWrite"/>) and the partially defined ones (any bit in
    /// <see cref="IrEffects.MayWrite"/> but not fully must-written; token writes are always partial).
    /// </summary>
    public static (uint Full, uint Partial) WriteBits(IrEffects effects) {
        uint full = 0;
        uint partial = 0;
        for (int v = 0; v <= (int)IrSsaValue.Direction; v++) {
            CpuValues mask = MaskOf((IrSsaValue)v);
            if ((effects.MustWrite & mask) == mask) {
                full |= 1u << v;
            } else if ((effects.MayWrite & mask) != CpuValues.None) {
                partial |= 1u << v;
            }
        }
        return (full, partial | TokenBits(effects.TokenWrites));
    }
}
