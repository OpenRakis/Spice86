namespace Spice86.Core.Backend.Audio.IirFilters;

internal class HighShelf : RbjBase {
    public void SetupN(
        double cutoffFrequency,
        double gainDb,
        double shelfSlope = 1) {
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * MathSupplement.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double al = sn / 2 * Math.Sqrt((a + 1 / a) * (1 / shelfSlope - 1) + 2);
        double sq = 2 * Math.Sqrt(a) * al;
        double b0 = a * (a + 1 + (a - 1) * cs + sq);
        double b1 = -2 * a * (a - 1 + (a + 1) * cs);
        double b2 = a * (a + 1 + (a - 1) * cs - sq);
        double a0 = a + 1 - (a - 1) * cs + sq;
        double a1 = 2 * (a - 1 - (a + 1) * cs);
        double a2 = a + 1 - (a - 1) * cs - sq;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(double sampleRate,
        double cutoffFrequency,
        double gainDb,
        double shelfSlope = 1) {
        SetupN(cutoffFrequency / sampleRate, gainDb, shelfSlope);
    }
}