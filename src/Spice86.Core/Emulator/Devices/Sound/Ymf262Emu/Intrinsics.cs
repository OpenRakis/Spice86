namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;

using System.Runtime.Intrinsics.X86;

/// <summary>
/// Provides low-level hardware intrinsics for use in performance-critical code.
/// </summary>
internal static class Intrinsics {
    
    /// <summary>
    /// Extracts a contiguous sequence of bits from a given integer value, starting from a specified index
    /// and with a specified length. If the BMI1 instruction set is supported by the current CPU, this method
    /// will use hardware acceleration via the BitFieldExtract method provided by the X86.Bmi1 class. Otherwise,
    /// this method will perform the extraction using software logic.
    /// </summary>
    /// <param name="value">The integer value from which to extract the bits.</param>
    /// <param name="start">The index of the starting bit to extract.</param>
    /// <param name="length">The number of bits to extract.</param>
    /// <param name="mask">A mask to apply to the value before extraction.</param>
    /// <returns>The extracted bits, packed into an unsigned integer.</returns>
    public static uint ExtractBits(uint value, byte start, byte length, uint mask) {
        if (Bmi1.IsSupported) {
            return Bmi1.BitFieldExtract(value, start, length);
        } else {
            return (value & mask) >> start;
        }
    }

    /// <summary>
    /// Returns the base-2 logarithm of a specified number.
    /// </summary>
    /// <param name="x">The number whose logarithm is to be computed.</param>
    /// <returns>The base-2 logarithm of x.</returns>
    public static double Log2(double x) => Math.Log2(x);
}