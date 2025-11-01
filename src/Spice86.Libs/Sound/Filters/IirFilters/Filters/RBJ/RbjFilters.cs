namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.RBJ;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.State;

public abstract class RbjFilterBase<TState> : BiquadFilterBase<TState>
    where TState : struct, ISectionState {
    internal static double OneOverSqrtTwo => 1.0 / Math.Sqrt(2.0);

    protected void SetupLowPass(double cutoffFrequency, double q) {
        double w0 = 2.0 * MathEx.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double alpha = sn / (2.0 * q);
        double b0 = (1.0 - cs) * 0.5;
        double b1 = 1.0 - cs;
        double b2 = (1.0 - cs) * 0.5;
        double a0 = 1.0 + alpha;
        double a1 = -2.0 * cs;
        double a2 = 1.0 - alpha;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    protected void SetupHighPass(double cutoffFrequency, double q) {
        double w0 = 2.0 * MathEx.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double alpha = sn / (2.0 * q);
        double b0 = (1.0 + cs) * 0.5;
        double b1 = -(1.0 + cs);
        double b2 = (1.0 + cs) * 0.5;
        double a0 = 1.0 + alpha;
        double a1 = -2.0 * cs;
        double a2 = 1.0 - alpha;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    protected void SetupBandPass1(double centerFrequency, double bandWidth) {
        double w0 = 2.0 * MathEx.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double alpha = sn / (2.0 * bandWidth);
        double gain = bandWidth * alpha;
        const double b1 = 0.0;
        double b2 = -gain;
        double a0 = 1.0 + alpha;
        double a1 = -2.0 * cs;
        double a2 = 1.0 - alpha;
        SetCoefficients(a0, a1, a2, gain, b1, b2);
    }

    protected void SetupBandPass2(double centerFrequency, double bandWidth) {
        double w0 = 2.0 * MathEx.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double alpha = sn / (2.0 * bandWidth);
        const double b1 = 0.0;
        double b2 = -alpha;
        double a0 = 1.0 + alpha;
        double a1 = -2.0 * cs;
        double a2 = 1.0 - alpha;
        SetCoefficients(a0, a1, a2, alpha, b1, b2);
    }

    protected void SetupBandStop(double centerFrequency, double bandWidth) {
        double w0 = 2.0 * MathEx.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double alpha = sn / (2.0 * bandWidth);
        const double b0 = 1.0;
        double b1 = -2.0 * cs;
        const double b2 = 1.0;
        double a0 = 1.0 + alpha;
        double a1 = -2.0 * cs;
        double a2 = 1.0 - alpha;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    protected void SetupNotch(double centerFrequency, double q) {
        double w0 = 2.0 * MathEx.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double r = Math.Exp(-(w0 / 2.0) / q);
        const double b0 = 1.0;
        double b1 = -2.0 * cs;
        const double b2 = 1.0;
        const double a0 = 1.0;
        double a1 = -2.0 * r * cs;
        double a2 = r * r;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    protected void SetupLowShelf(double cutoffFrequency, double gainDb, double shelfSlope) {
        double aGain = Math.Pow(10.0, gainDb / 40.0);
        double w0 = 2.0 * MathEx.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double alpha = sn / 2.0 * Math.Sqrt(((aGain + (1.0 / aGain)) * ((1.0 / shelfSlope) - 1.0)) + 2.0);
        double sqrtA = Math.Sqrt(aGain);
        double sq = 2.0 * sqrtA * alpha;
        double b0 = aGain * (aGain + 1.0 - ((aGain - 1.0) * cs) + sq);
        double b1 = 2.0 * aGain * (aGain - 1.0 - ((aGain + 1.0) * cs));
        double b2 = aGain * (aGain + 1.0 - ((aGain - 1.0) * cs) - sq);
        double a0 = aGain + 1.0 + ((aGain - 1.0) * cs) + sq;
        double a1 = -2.0 * (aGain - 1.0 + ((aGain + 1.0) * cs));
        double a2 = aGain + 1.0 + ((aGain - 1.0) * cs) - sq;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    protected void SetupHighShelf(double cutoffFrequency, double gainDb, double shelfSlope) {
        double aGain = Math.Pow(10.0, gainDb / 40.0);
        double w0 = 2.0 * MathEx.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double alpha = sn / 2.0 * Math.Sqrt(((aGain + (1.0 / aGain)) * ((1.0 / shelfSlope) - 1.0)) + 2.0);
        double sqrtA = Math.Sqrt(aGain);
        double sq = 2.0 * sqrtA * alpha;
        double b0 = aGain * (aGain + 1.0 + ((aGain - 1.0) * cs) + sq);
        double b1 = -2.0 * aGain * (aGain - 1.0 + ((aGain + 1.0) * cs));
        double b2 = aGain * (aGain + 1.0 + ((aGain - 1.0) * cs) - sq);
        double a0 = aGain + 1.0 - ((aGain - 1.0) * cs) + sq;
        double a1 = 2.0 * (aGain - 1.0 - ((aGain + 1.0) * cs));
        double a2 = aGain + 1.0 - ((aGain - 1.0) * cs) - sq;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    protected void SetupBandShelf(double centerFrequency, double gainDb, double bandWidth) {
        double aGain = Math.Pow(10.0, gainDb / 40.0);
        double w0 = 2.0 * MathEx.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double alpha = sn * Math.Sinh(MathEx.DoubleLn2 / 2.0 * bandWidth * w0 / sn);
        if (double.IsNaN(alpha)) {
            throw new ArgumentException("No solution available for these parameters.");
        }

        double b0 = 1.0 + (alpha * aGain);
        double b1 = -2.0 * cs;
        double b2 = 1.0 - (alpha * aGain);
        double a0 = 1.0 + (alpha / aGain);
        double a1 = -2.0 * cs;
        double a2 = 1.0 - (alpha / aGain);
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    protected void SetupAllPass(double phaseFrequency, double q) {
        double w0 = 2.0 * MathEx.DoublePi * phaseFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double alpha = sn / (2.0 * q);
        double b0 = 1.0 - alpha;
        double b1 = -2.0 * cs;
        double b2 = 1.0 + alpha;
        double a0 = 1.0 + alpha;
        double a1 = -2.0 * cs;
        double a2 = 1.0 - alpha;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }
}