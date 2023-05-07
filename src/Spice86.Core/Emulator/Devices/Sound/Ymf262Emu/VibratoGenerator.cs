namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;

/// <summary>
/// Generates a vibrato waveform for use in the YMF262Emu sound emulator.
/// </summary>
internal static class VibratoGenerator {
    /// <summary>
    /// The length of the generated vibrato waveform.
    /// </summary>
    public const int Length = 8192;

    private static readonly double Cent = Math.Pow(Math.Pow(2, 1 / 12.0), 1 / 100.0);
    private static readonly double Dvb0 = Math.Pow(Cent, 7);
    private static readonly double Dvb1 = Math.Pow(Cent, 14);

    /// <summary>
    /// The values of the generated vibrato waveform.
    /// </summary>
    private static readonly double[] Values = {
        1,
        Math.Sqrt(Dvb0),
        Dvb0,
        Math.Sqrt(Dvb0),
        1,
        1 / Math.Sqrt(Dvb0),
        1 / Dvb0,
        1 / Math.Sqrt(Dvb0),

        1,
        Math.Sqrt(Dvb1),
        Dvb1,
        Math.Sqrt(Dvb1),
        1,
        1 / Math.Sqrt(Dvb1),
        1 / Dvb1,
        1 / Math.Sqrt(Dvb1)
    };

    /// <summary>
    /// Returns the value of the generated vibrato waveform at the specified index and depth.
    /// </summary>
    /// <param name="dvb">The depth of the vibrato waveform.</param>
    /// <param name="i">The index of the vibrato waveform to retrieve.</param>
    /// <returns>The value of the generated vibrato waveform at the specified index and depth.</returns>
    public static double GetValue(int dvb, int i) => Values[(dvb * 8) + Intrinsics.ExtractBits((uint)i, 10, 3, 0x1C00)];
}