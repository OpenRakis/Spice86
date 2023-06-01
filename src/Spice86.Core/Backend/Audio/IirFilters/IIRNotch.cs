namespace Spice86.Core.Backend.Audio.IirFilters;

internal class IirNotch : RbjBase {
    public void SetupN(
        double centerFrequency,
        double qFactor = 10) {
        double w0 = 2 * MathSupplement.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double r = Math.Exp(-(w0 / 2) / qFactor);
        const double b0 = 1;
        double b1 = -2 * cs;
        const double b2 = 1;
        const double a0 = 1;
        double a1 = -2 * r * cs;
        double a2 = r * r;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(
        double sampleRate,
        double centerFrequency,
        double qFactor = 10) {
        SetupN(centerFrequency / sampleRate, qFactor);
    }
}