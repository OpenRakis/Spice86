namespace Spice86.Libs.Sound.Filters.IirFilters.Common;

using Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;

using System.Numerics;

public sealed class Biquad {
    internal double A0 = 1.0;
    internal double A1;
    internal double A2;
    internal double B0 = 1.0;
    internal double B1;
    internal double B2;

    internal Complex Response(double normalizedFrequency) {
        double a0 = GetA0();
        double a1 = GetA1();
        double a2 = GetA2();
        double b0 = GetB0();
        double b1 = GetB1();
        double b2 = GetB2();

        double w = 2.0 * MathEx.DoublePi * normalizedFrequency;
        var czn1 = Complex.FromPolarCoordinates(1.0, -w);
        var czn2 = Complex.FromPolarCoordinates(1.0, -2.0 * w);
        var ct = new Complex(b0 / a0, 0.0);
        Complex cb = Complex.One;
        ct = MathEx.AddMul(ct, b1 / a0, czn1);
        ct = MathEx.AddMul(ct, b2 / a0, czn2);
        cb = MathEx.AddMul(cb, a1 / a0, czn1);
        cb = MathEx.AddMul(cb, a2 / a0, czn2);
        return ct / cb;
    }

    internal IReadOnlyList<PoleZeroPair> GetPoleZeroPairs() {
        return [new BiquadPoleState(this)];
    }

    internal double GetA0() {
        return A0;
    }

    internal double GetA1() {
        return A1 * A0;
    }

    internal double GetA2() {
        return A2 * A0;
    }

    internal double GetB0() {
        return B0 * A0;
    }

    internal double GetB1() {
        return B1 * A0;
    }

    internal double GetB2() {
        return B2 * A0;
    }

    internal void SetCoefficients(double a0, double a1, double a2, double b0, double b1, double b2) {
        if (double.IsNaN(a0)) {
            throw new ArgumentException("a0 is NaN");
        }

        if (double.IsNaN(a1)) {
            throw new ArgumentException("a1 is NaN");
        }

        if (double.IsNaN(a2)) {
            throw new ArgumentException("a2 is NaN");
        }

        if (double.IsNaN(b0)) {
            throw new ArgumentException("b0 is NaN");
        }

        if (double.IsNaN(b1)) {
            throw new ArgumentException("b1 is NaN");
        }

        if (double.IsNaN(b2)) {
            throw new ArgumentException("b2 is NaN");
        }

        A0 = a0;
        A1 = a1 / a0;
        A2 = a2 / a0;
        B0 = b0 / a0;
        B1 = b1 / a0;
        B2 = b2 / a0;
    }

    internal void SetOnePole(Complex pole, Complex zero) {
        if (pole.Imaginary != 0.0) {
            throw new ArgumentException("Imaginary part of pole is non-zero.");
        }

        if (zero.Imaginary != 0.0) {
            throw new ArgumentException("Imaginary part of zero is non-zero.");
        }

        SetCoefficients(1.0, -pole.Real, 0.0, 1.0, -zero.Real, 0.0);
    }

    internal void SetTwoPole(Complex pole1, Complex zero1, Complex pole2, Complex zero2) {
        const string poleErr = "imaginary parts of both poles need to be 0 or complex conjugate";
        const string zeroErr = "imaginary parts of both zeros need to be 0 or complex conjugate";

        double a1;
        double a2;

        if (pole1.Imaginary != 0.0) {
            if (pole2 != Complex.Conjugate(pole1)) {
                throw new ArgumentException(poleErr);
            }

            a1 = -2.0 * pole1.Real;
            a2 = Complex.Abs(pole1) * Complex.Abs(pole1);
        } else {
            if (pole2.Imaginary != 0.0) {
                throw new ArgumentException(poleErr);
            }

            a1 = -(pole1.Real + pole2.Real);
            a2 = pole1.Real * pole2.Real;
        }

        double b1;
        double b2;

        if (zero1.Imaginary != 0.0) {
            if (zero2 != Complex.Conjugate(zero1)) {
                throw new ArgumentException(zeroErr);
            }

            b1 = -2.0 * zero1.Real;
            b2 = Complex.Abs(zero1) * Complex.Abs(zero1);
        } else {
            if (zero2.Imaginary != 0.0) {
                throw new ArgumentException(zeroErr);
            }

            b1 = -(zero1.Real + zero2.Real);
            b2 = zero1.Real * zero2.Real;
        }

        SetCoefficients(1.0, a1, a2, 1.0, b1, b2);
    }

    internal void SetPoleZeroPair(PoleZeroPair pair) {
        if (pair.IsSinglePole()) {
            SetOnePole(pair.Poles.First, pair.Zeros.First);
        } else {
            SetTwoPole(pair.Poles.First, pair.Zeros.First, pair.Poles.Second, pair.Zeros.Second);
        }
    }

    internal void SetPoleZeroForm(BiquadPoleState state) {
        SetPoleZeroPair(state);
        ApplyScale(state.Gain);
    }

    internal void SetIdentity() {
        SetCoefficients(1.0, 0.0, 0.0, 1.0, 0.0, 0.0);
    }

    internal void ApplyScale(double scale) {
        B0 *= scale;
        B1 *= scale;
        B2 *= scale;
    }
}

internal readonly struct BiquadPoleState {
    internal readonly PoleZeroPair Pair;
    internal readonly double Gain;

    internal BiquadPoleState(Biquad s) {
        double a0 = s.GetA0();
        double a1 = s.GetA1();
        double a2 = s.GetA2();
        double b0 = s.GetB0();
        double b1 = s.GetB1();
        double b2 = s.GetB2();

        Complex polesFirst;
        Complex polesSecond;
        Complex zerosFirst;
        Complex zerosSecond;

        if (a2 == 0.0 && b2 == 0.0) {
            polesFirst = -a1;
            zerosFirst = -b0 / b1;
            polesSecond = Complex.Zero;
            zerosSecond = Complex.Zero;
        } else {
            var c = Complex.Sqrt(new Complex((a1 * a1) - (4.0 * a0 * a2), 0.0));
            double d = 2.0 * a0;
            polesFirst = -(a1 + c) / d;
            polesSecond = (c - a1) / d;
            if (MathEx.IsNaN(polesFirst) || MathEx.IsNaN(polesSecond)) {
                throw new ArgumentException("poles are NaN");
            }

            var cz = Complex.Sqrt(new Complex((b1 * b1) - (4.0 * b0 * b2), 0.0));
            double dz = 2.0 * b0;
            zerosFirst = -(b1 + cz) / dz;
            zerosSecond = (cz - b1) / dz;
            if (MathEx.IsNaN(zerosFirst) || MathEx.IsNaN(zerosSecond)) {
                throw new ArgumentException("zeros are NaN");
            }
        }

        Pair = new PoleZeroPair(polesFirst, zerosFirst, polesSecond, zerosSecond);
        Gain = b0 / a0;
    }

    public static implicit operator PoleZeroPair(BiquadPoleState state) {
        return state.Pair;
    }
}