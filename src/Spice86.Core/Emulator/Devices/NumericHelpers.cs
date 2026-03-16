namespace Spice86.Core.Emulator.Devices;

using System.Runtime.CompilerServices;

/// <summary>
///     Deterministic numeric helpers used by the Device Scheduler and PIT chip.
/// </summary>
internal static class NumericConverters {
    /// <summary>
    ///     Converts a decimal <see cref="ushort" /> value to its packed BCD representation.
    /// </summary>
    /// <param name="val">Decimal value to convert. Each digit must be less than 10.</param>
    /// <returns>BCD-encoded unsigned short containing the decimal digits.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort DecimalToBcd(ushort val) {
        ushort first = (ushort)(val % 10);
        ushort second = (ushort)(val / 10 % 10);
        ushort third = (ushort)(val / 100 % 10);
        ushort fourth = (ushort)(val / 1000 % 10);
        return (ushort)(first + (second << 4) + (third << 8) + (fourth << 12));
    }

    /// <summary>
    ///     Converts a packed BCD <see cref="ushort" /> value to its decimal representation.
    /// </summary>
    /// <param name="val">BCD-encoded value to convert.</param>
    /// <returns>Unsigned short containing the decoded decimal value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort BcdToDecimal(ushort val) {
        ushort ones = (ushort)(val & 0x000F);
        ushort tens = (ushort)((val & 0x00F0) >> 4);
        ushort hundreds = (ushort)((val & 0x0F00) >> 8);
        ushort thousands = (ushort)((val & 0xF000) >> 12);
        return (ushort)((thousands * 1000) + (hundreds * 100) + (tens * 10) + ones);
    }
}