namespace Spice86.Core.Emulator.Devices;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

/// <summary>
///     Deterministic numeric helpers used by the Device Scheduler and PIT chip.
/// </summary>
internal static class NumericConverters {
    /// <summary>
    ///     Casts <paramref name="value" /> to the target integer type using checked semantics.
    /// </summary>
    /// <typeparam name="TTarget">Target integer type to cast to.</typeparam>
    /// <typeparam name="TSource">Source integer type to cast from.</typeparam>
    /// <param name="value">Source value to cast.</param>
    /// <returns>The source value converted to the target type.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TTarget CheckCast<TTarget, TSource>(TSource value)
        where TTarget : struct, IBinaryInteger<TTarget>
        where TSource : struct, IBinaryInteger<TSource> {
        try {
            return TTarget.CreateChecked(value);
        } catch (OverflowException) {
            Debug.Assert(false, "CheckCast range assertion failed");
            return TTarget.CreateTruncating(value);
        }
    }

    /// <summary>
    ///     Rounds the floating-point value to the nearest integer using away-from-zero semantics.
    /// </summary>
    /// <param name="x">Floating-point value to convert.</param>
    /// <returns>The rounded 32-bit integer value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundToNearestInt(double x) {
        Debug.Assert(double.IsFinite(x));
        Debug.Assert(x >= int.MinValue);
        Debug.Assert(x <= int.MaxValue);
        return (int)Math.Round(x, MidpointRounding.AwayFromZero);
    }

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