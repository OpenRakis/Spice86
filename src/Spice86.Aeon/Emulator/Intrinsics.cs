namespace Spice86.Aeon.Emulator; 
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// Contains static methods that use intrinsic functions for efficient bit manipulation and extraction operations. 
/// </summary>
public static class Intrinsics {
    /// <summary>
    /// Extracts a range of bits from a value.
    /// </summary>
    /// <param name="value">The value to extract bits from.</param>
    /// <param name="start">The starting bit index (inclusive).</param>
    /// <param name="length">The number of bits to extract.</param>
    /// <param name="mask">A mask to apply before extracting the bits.</param>
    /// <returns>The extracted bits.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ExtractBits(uint value, byte start, byte length, uint mask) {
        return Bmi1.IsSupported ? Bmi1.BitFieldExtract(value, start, length) : (value & mask) >>> start;
    }
    
    /// <summary>
    /// Computes the bitwise AND of two values with the bits in <paramref name="b"/> inverted.
    /// </summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns>The result of <paramref name="a"/> &amp; ~<paramref name="b"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint AndNot(uint a, uint b) {
        return Bmi1.IsSupported ? Bmi1.AndNot(b, a) : a & ~b;
    }
    
    /// <summary>
    /// Resets the lowest set bit of a value.
    /// </summary>
    /// <param name="value">The value to reset the lowest set bit of.</param>
    /// <returns>The value with the lowest set bit reset.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ResetLowestSetBit(uint value) {
        if (Bmi1.IsSupported) {
            return Bmi1.ResetLowestSetBit(value);
        }
        else {
            int trailingZeroCount = BitOperations.TrailingZeroCount(value);
            return trailingZeroCount < 32 ? value & ~(1u << trailingZeroCount) : 0;
        }
    }
    
    /// <summary>
    /// Returns the high byte of a 16-bit value.
    /// </summary>
    /// <param name="value">The value to extract the high byte from.</param>
    /// <returns>The high byte of the value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte HighByte(ushort value) {
        unsafe {
            return ((byte*)&value)[1];
        }
    }
    
    /// <summary>
    /// Returns the low byte of a 16-bit value.
    /// </summary>
    /// <param name="value">The value to extract the low byte from.</param>
    /// <returns>The low byte of the value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte LowByte(ushort value) => (byte)value;
    
    /// <summary>
    /// Returns the high word of a 32-bit value.
    /// </summary>
    /// <param name="value">The value to extract the high word from.</param>
    /// <returns>The high word of the value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort HighWord(uint value) {
        unsafe {
            return ((ushort*)&value)[1];
        }
    }
    
    /// <summary>
    /// Returns the low 32 bits (double word) of the input 64-bit unsigned integer value.
    /// </summary>
    /// <param name="value">The input 64-bit unsigned integer value.</param>
    /// <returns>The low 32 bits (double word) of the input value as an unsigned integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint LowDWord(ulong value) => (uint)value;
}