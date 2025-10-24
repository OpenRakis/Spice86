namespace Spice86.Libs.Sound.Filters.IirFilters.Common.Transforms;

using Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;

using System.Numerics;

internal sealed class LowPassTransform {
    private readonly double _f;

    internal LowPassTransform(double fc, LayoutBase digital, LayoutBase analog) {
        if (!(fc < 0.5)) {
            throw new ArgumentException("The cutoff frequency needs to be below the Nyquist frequency.");
        }

        if (fc < 0.0) {
            throw new ArgumentException("Cutoff frequency is negative.");
        }

        digital.Reset();
        _f = Math.Tan(MathEx.DoublePi * fc);

        int numPoles = analog.GetNumPoles();
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; i++) {
            ref readonly PoleZeroPair pair = ref analog[i];
            digital.AddPoleZeroConjugatePairs(Transform(pair.Poles.First), Transform(pair.Zeros.First));
        }

        if ((numPoles & 1) != 0) {
            ref readonly PoleZeroPair pair = ref analog[pairs];
            digital.Add(Transform(pair.Poles.First), Transform(pair.Zeros.First));
        }

        digital.SetNormal(analog.GetNormalW(), analog.GetNormalGain());
    }

    private Complex Transform(Complex c) {
        if (MathEx.IsInfinity(c)) {
            return new Complex(-1.0, 0.0);
        }

        c = _f * c;
        return (Complex.One + c) / (Complex.One - c);
    }
}

internal sealed class HighPassTransform {
    private readonly double _f;

    internal HighPassTransform(double fc, LayoutBase digital, LayoutBase analog) {
        if (!(fc < 0.5)) {
            throw new ArgumentException("The cutoff frequency needs to be below the Nyquist frequency.");
        }

        if (fc < 0.0) {
            throw new ArgumentException("Cutoff frequency is negative.");
        }

        digital.Reset();
        _f = 1.0 / Math.Tan(MathEx.DoublePi * fc);

        int numPoles = analog.GetNumPoles();
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; i++) {
            ref readonly PoleZeroPair pair = ref analog[i];
            digital.AddPoleZeroConjugatePairs(Transform(pair.Poles.First), Transform(pair.Zeros.First));
        }

        if ((numPoles & 1) != 0) {
            ref readonly PoleZeroPair pair = ref analog[pairs];
            digital.Add(Transform(pair.Poles.First), Transform(pair.Zeros.First));
        }

        digital.SetNormal(MathEx.DoublePi - analog.GetNormalW(), analog.GetNormalGain());
    }

    private Complex Transform(Complex c) {
        if (MathEx.IsInfinity(c)) {
            return Complex.One;
        }

        c = _f * c;
        return -(Complex.One + c) / (Complex.One - c);
    }
}

internal sealed class BandPassTransform {
    private readonly double _a2;
    private readonly double _ab2;
    private readonly double _b;
    private readonly double _b2;

    internal BandPassTransform(double fc, double fw, LayoutBase digital, LayoutBase analog) {
        if (!(fc < 0.5)) {
            throw new ArgumentException("The cutoff frequency needs to be below the Nyquist frequency.");
        }

        if (fc < 0.0) {
            throw new ArgumentException("Cutoff frequency is negative.");
        }

        digital.Reset();

        double ww = 2.0 * MathEx.DoublePi * fw;
        double wc2 = (2.0 * MathEx.DoublePi * fc) - (ww / 2.0);
        double wc = wc2 + ww;

        if (wc2 < 1e-8) {
            wc2 = 1e-8;
        }

        if (wc > MathEx.DoublePi - 1e-8) {
            wc = MathEx.DoublePi - 1e-8;
        }

        double a = Math.Cos((wc + wc2) * 0.5) / Math.Cos((wc - wc2) * 0.5);
        _b = 1.0 / Math.Tan((wc - wc2) * 0.5);
        _a2 = a * a;
        _b2 = _b * _b;
        double ab = a * _b;
        _ab2 = 2.0 * ab;

        int numPoles = analog.GetNumPoles();
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; i++) {
            ref readonly PoleZeroPair pair = ref analog[i];
            ComplexPair pole = Transform(pair.Poles.First);
            ComplexPair zero = Transform(pair.Zeros.First);
            digital.AddPoleZeroConjugatePairs(pole.First, zero.First);
            digital.AddPoleZeroConjugatePairs(pole.Second, zero.Second);
        }

        if ((numPoles & 1) != 0) {
            ComplexPair poles = Transform(analog[pairs].Poles.First);
            ComplexPair zeros = Transform(analog[pairs].Zeros.First);
            digital.Add(poles, zeros);
        }

        double wn = analog.GetNormalW();
        double normalW = 2.0 * Math.Atan(Math.Sqrt(Math.Tan((wc + wn) * 0.5) * Math.Tan((wc2 + wn) * 0.5)));
        digital.SetNormal(normalW, analog.GetNormalGain());
    }

    private ComplexPair Transform(Complex c) {
        if (MathEx.IsInfinity(c)) {
            return new ComplexPair(new Complex(-1.0, 0.0), new Complex(1.0, 0.0));
        }

        c = (Complex.One + c) / (Complex.One - c);

        Complex v = Complex.Zero;
        v = MathEx.AddMul(v, 4.0 * ((_b2 * (_a2 - 1.0)) + 1.0), c);
        v += 8.0 * ((_b2 * (_a2 - 1.0)) - 1.0);
        v *= c;
        v += 4.0 * ((_b2 * (_a2 - 1.0)) + 1.0);
        v = Complex.Sqrt(v);

        Complex u = -v;
        u = MathEx.AddMul(u, _ab2, c);
        u += _ab2;

        v = MathEx.AddMul(v, _ab2, c);
        v += _ab2;

        Complex d = Complex.Zero;
        d = MathEx.AddMul(d, 2.0 * (_b - 1.0), c);
        d += 2.0 * (1.0 + _b);

        Complex first = u / d;
        Complex second = v / d;
        return new ComplexPair(first, second);
    }
}

internal sealed class BandStopTransform {
    private readonly double _a;
    private readonly double _a2;
    private readonly double _b;
    private readonly double _b2;

    internal BandStopTransform(double fc, double fw, LayoutBase digital, LayoutBase analog) {
        if (!(fc < 0.5)) {
            throw new ArgumentException("The cutoff frequency needs to be below the Nyquist frequency.");
        }

        if (fc < 0.0) {
            throw new ArgumentException("Cutoff frequency is negative.");
        }

        digital.Reset();

        double ww = 2.0 * MathEx.DoublePi * fw;
        double wc2 = (2.0 * MathEx.DoublePi * fc) - (ww / 2.0);
        double wc = wc2 + ww;

        if (wc2 < 1e-8) {
            wc2 = 1e-8;
        }

        if (wc > MathEx.DoublePi - 1e-8) {
            wc = MathEx.DoublePi - 1e-8;
        }

        _a = Math.Cos((wc + wc2) * 0.5) / Math.Cos((wc - wc2) * 0.5);
        _b = Math.Tan((wc - wc2) * 0.5);
        _a2 = _a * _a;
        _b2 = _b * _b;

        int numPoles = analog.GetNumPoles();
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; i++) {
            ref readonly PoleZeroPair pair = ref analog[i];
            ComplexPair poles = Transform(pair.Poles.First);
            ComplexPair zeros = Transform(pair.Zeros.First);

            if (zeros.Second == zeros.First) {
                zeros.Second = Complex.Conjugate(zeros.First);
            }

            digital.AddPoleZeroConjugatePairs(poles.First, zeros.First);
            digital.AddPoleZeroConjugatePairs(poles.Second, zeros.Second);
        }

        if ((numPoles & 1) != 0) {
            ComplexPair poles = Transform(analog[pairs].Poles.First);
            ComplexPair zeros = Transform(analog[pairs].Zeros.First);
            digital.Add(poles, zeros);
        }

        digital.SetNormal(fc < 0.25 ? MathEx.DoublePi : 0.0, analog.GetNormalGain());
    }

    private ComplexPair Transform(Complex c) {
        if (MathEx.IsInfinity(c)) {
            c = new Complex(-1.0, 0.0);
        } else {
            c = (Complex.One + c) / (Complex.One - c);
        }

        Complex u = Complex.Zero;
        u = MathEx.AddMul(u, 4.0 * (_b2 + _a2 - 1.0), c);
        u += 8.0 * (_b2 - _a2 + 1.0);
        u *= c;
        u += 4.0 * (_a2 + _b2 - 1.0);
        u = Complex.Sqrt(u);

        Complex v = -0.5 * u;
        v += _a;
        v = MathEx.AddMul(v, -_a, c);

        u *= 0.5;
        u += _a;
        u = MathEx.AddMul(u, -_a, c);

        var d = new Complex(_b + 1.0, 0.0);
        d = MathEx.AddMul(d, _b - 1.0, c);

        return new ComplexPair(u / d, v / d);
    }
}