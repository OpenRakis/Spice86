namespace Spice86.Core.Backend.Audio.IirFilters;

internal class AllPass : RbjBase {
    private const double OneSqrtTwo = 0.707106781;

    public void SetupN(
        double phaseFrequency,
        double q = OneSqrtTwo) {
        double w0 = 2 * MathSupplement.DoublePi * phaseFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double al = sn / (2 * q);
        double b0 = 1 - al;
        double b1 = -2 * cs;
        double b2 = 1 + al;
        double a0 = 1 + al;
        double a1 = -2 * cs;
        double a2 = 1 - al;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(double sampleRate,
        double phaseFrequency,
        double q = OneSqrtTwo) {
        SetupN(phaseFrequency / sampleRate, q);
    }
}