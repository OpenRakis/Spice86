namespace Spice86.Core.Backend.Audio.IirFilters;

internal class BandStop : RbjBase {
    public void SetupN(
        double centerFrequency,
        double bandWidth) {
        double w0 = 2 * MathSupplement.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double al = sn / (2 * bandWidth);
        double b0 = 1;
        double b1 = -2 * cs;
        double b2 = 1;
        double a0 = 1 + al;
        double a1 = -2 * cs;
        double a2 = 1 - al;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(
        double sampleRate,
        double centerFrequency,
        double bandWidth) {
        SetupN(centerFrequency / sampleRate, bandWidth);
    }
}