namespace Spice86.Libs.Sound.Common;

/// <summary>
///     Provides helper methods for common audio math conversions.
/// </summary>
internal static class MathEx {
    /// <summary>
    ///     Converts a decibel value to a linear gain factor.
    /// </summary>
    /// <param name="decibel">The decibel value to convert.</param>
    /// <returns>The equivalent linear gain factor.</returns>
    internal static float DecibelToGain(float decibel) {
        return (float)Math.Pow(10.0f, decibel / 20.0f);
    }

    /// <summary>
    ///     Converts a linear gain factor to decibels.
    /// </summary>
    /// <param name="gain">The linear gain factor to convert. Values less than or equal to zero yield negative infinity or NaN.</param>
    /// <returns>The equivalent value expressed in decibels.</returns>
    internal static float GainToDecibel(float gain) {
        return 20.0f * (float)Math.Log(gain, 10.0f);
    }
}