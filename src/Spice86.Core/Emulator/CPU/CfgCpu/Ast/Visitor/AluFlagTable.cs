namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The status-flag effect of each <c>Alu8</c> / <c>Alu16</c> / <c>Alu32</c> method, as implemented by the
/// emulator's ALU (not as documented by Intel): generated code is compared with the interpreter.
/// </summary>
internal static class AluFlagTable {
    private const StatusFlags ZeroParitySign = StatusFlags.Zero | StatusFlags.Parity | StatusFlags.Sign;
    private const StatusFlags ShiftWrites = StatusFlags.Carry | ZeroParitySign | StatusFlags.Overflow;
    private const StatusFlags RotateWrites = StatusFlags.Carry | StatusFlags.Overflow;
    private const ulong ShiftCountMask = 0x1F;

    /// <summary>
    /// The effect of <paramref name="operation"/> at <paramref name="width"/>. <paramref name="count"/> is the
    /// constant shift or rotate count, or <c>null</c> when it is not known; other operations ignore it.
    /// Returns <c>false</c> (and <see cref="FlagEffect.Unknown"/>) for a method the table does not know.
    /// </summary>
    public static bool TryGetEffect(string operation, BitWidth width, ulong? count, out FlagEffect effect) {
        FlagEffect? known = operation switch {
            "Add" or "Sub" or "And" or "Or" or "Xor" or "Imul" => FlagEffect.Writing(StatusFlags.All),
            "Adc" or "Sbb" => new FlagEffect(StatusFlags.Carry, StatusFlags.All, StatusFlags.All),
            "Inc" or "Dec" => FlagEffect.Writing(StatusFlags.All & ~StatusFlags.Carry),
            "Mul" => FlagEffect.Writing(StatusFlags.Overflow | StatusFlags.Carry | StatusFlags.Zero),
            "Div" or "Idiv" => FlagEffect.None,
            "UpdateFlags" => FlagEffect.Writing(ZeroParitySign),
            "Shl" or "Shr" or "Sar" or "Shld" or "Shrd" => CountDependent(count, ShiftWrites),
            "Rol" or "Ror" => CountDependent(count, RotateWrites),
            "Rcl" => RotateThroughCarry(width, count, StatusFlags.Carry),
            "Rcr" => RotateThroughCarry(width, count, StatusFlags.None),
            _ => null
        };
        effect = known ?? FlagEffect.Unknown;
        return known is not null;
    }

    private static FlagEffect CountDependent(ulong? count, StatusFlags writes) {
        if (count is null) {
            return new FlagEffect(StatusFlags.None, StatusFlags.None, writes);
        }
        if ((count.Value & ShiftCountMask) == 0) {
            return FlagEffect.None;
        }
        return FlagEffect.Writing(writes);
    }

    private static FlagEffect RotateThroughCarry(BitWidth width, ulong? count, StatusFlags readsWhenOnlyOverflow) {
        if (count is null) {
            return new FlagEffect(StatusFlags.Carry, StatusFlags.None, RotateWrites);
        }
        ulong masked = count.Value & ShiftCountMask;
        if (masked == 0) {
            return FlagEffect.None;
        }
        if (width != BitWidth.DWORD_32 && masked % ((ulong)width + 1) == 0) {
            return new FlagEffect(readsWhenOnlyOverflow, StatusFlags.Overflow, StatusFlags.Overflow);
        }
        return new FlagEffect(StatusFlags.Carry, RotateWrites, RotateWrites);
    }
}
