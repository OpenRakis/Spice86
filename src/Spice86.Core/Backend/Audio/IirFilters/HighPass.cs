namespace Spice86.Core.Backend.Audio.IirFilters;

internal class HighPass : RbjBase {
    private const double Onesqrt2 = 0.707106781;

    public void SetupN(
        double cutoffFrequency,
        double q = Onesqrt2) {
        double w0 = 2 * MathSupplement.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double al = sn / (2 * q);
        double b0 = (1 + cs) / 2;
        double b1 = -(1 + cs);
        double b2 = (1 + cs) / 2;
        double a0 = 1 + al;
        double a1 = -2 * cs;
        double a2 = 1 - al;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(
        double sampleRate,
        double cutoffFrequency,
        double q = Onesqrt2) {
        SetupN(cutoffFrequency / sampleRate, q);
    }
}