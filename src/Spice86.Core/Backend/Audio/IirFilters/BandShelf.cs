namespace Spice86.Core.Backend.Audio.IirFilters;

internal class BandShelf : RbjBase {
    public void SetupN(
        double centerFrequency,
        double gainDb,
        double bandWidth) {
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * MathSupplement.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double al = sn * Math.Sinh(MathSupplement.DoubleLn2 / 2 * bandWidth * w0 / sn);
        if (double.IsNaN(al)) {
            throw new("No solution available for these parameters.\n");
        }
        double b0 = 1 + al * a;
        double b1 = -2 * cs;
        double b2 = 1 - al * a;
        double a0 = 1 + al / a;
        double a1 = -2 * cs;
        double a2 = 1 - al / a;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(double sampleRate,
        double centerFrequency,
        double gainDb,
        double bandWidth) {
        SetupN(centerFrequency / sampleRate, gainDb, bandWidth);
    }
}