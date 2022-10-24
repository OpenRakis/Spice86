namespace Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;

internal static class VibratoGenerator
{
    public const int Length = 8192;
    private static readonly double Cent = Math.Pow(Math.Pow(2, 1 / 12.0), 1 / 100.0);
    private static readonly double DVB0 = Math.Pow(Cent, 7);
    private static readonly double DVB1 = Math.Pow(Cent, 14);
    private static readonly double[] Values =
    {
        1,
        Math.Sqrt(DVB0),
        DVB0,
        Math.Sqrt(DVB0),
        1,
        1 / Math.Sqrt(DVB0),
        1 / DVB0,
        1 / Math.Sqrt(DVB0),

        1,
        Math.Sqrt(DVB1),
        DVB1,
        Math.Sqrt(DVB1),
        1,
        1 / Math.Sqrt(DVB1),
        1 / DVB1,
        1 / Math.Sqrt(DVB1)
    };

    public static double GetValue(int dvb, int i) => Values[(dvb * 8) + Intrinsics.ExtractBits((uint)i, 10, 3, 0x1C00)];
}
