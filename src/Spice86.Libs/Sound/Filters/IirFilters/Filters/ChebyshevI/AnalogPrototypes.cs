namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.ChebyshevI;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;

using System.Numerics;

public sealed class AnalogLowPass : LayoutBase {
    private int _numPoles = -1;
    private double _rippleDb;

    public AnalogLowPass() {
        SetNormal(0.0, 1.0);
    }

    public void Design(int numPoles, double rippleDb) {
        if (_numPoles == numPoles && Math.Abs(_rippleDb - rippleDb) <= double.Epsilon) {
            return;
        }

        _numPoles = numPoles;
        _rippleDb = rippleDb;

        Reset();

        if (numPoles == 0) {
            return;
        }

        double eps = Math.Sqrt((1.0 / Math.Exp(-rippleDb * 0.1 * MathEx.DoubleLn10)) - 1.0);
        double v0 = MathEx.Asinh(1.0 / eps) / numPoles;
        double sinhV0 = -Math.Sinh(v0);
        double coshV0 = Math.Cosh(v0);

        int n2 = 2 * numPoles;
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; i++) {
            int k = (2 * i) + 1 - numPoles;
            double angle = k * MathEx.DoublePi / n2;
            double a = sinhV0 * Math.Cos(angle);
            double b = coshV0 * Math.Sin(angle);
            AddPoleZeroConjugatePairs(new Complex(a, b), MathEx.Infinity());
        }

        if ((numPoles & 1) != 0) {
            Add(new Complex(sinhV0, 0.0), MathEx.Infinity());
            SetNormal(0.0, 1.0);
        } else {
            double gain = Math.Pow(10.0, -rippleDb / 20.0);
            SetNormal(0.0, gain);
        }
    }
}

public sealed class AnalogLowShelf : LayoutBase {
    private double _gainDb;
    private int _numPoles = -1;
    private double _rippleDb;

    public AnalogLowShelf() {
        SetNormal(MathEx.DoublePi, 1.0);
    }

    public void Design(int numPoles, double gainDb, double rippleDb) {
        if (_numPoles == numPoles &&
            Math.Abs(_gainDb - gainDb) <= double.Epsilon &&
            Math.Abs(_rippleDb - rippleDb) <= double.Epsilon) {
            return;
        }

        _numPoles = numPoles;
        _gainDb = gainDb;
        _rippleDb = rippleDb;

        Reset();

        if (numPoles == 0) {
            return;
        }

        double localGainDb = -gainDb;
        double localRippleDb = rippleDb;

        if (localRippleDb >= Math.Abs(localGainDb)) {
            localRippleDb = Math.Abs(localGainDb);
        }

        if (localGainDb < 0.0) {
            localRippleDb = -localRippleDb;
        }

        double g = Math.Pow(10.0, localGainDb / 20.0);
        double gb = Math.Pow(10.0, (localGainDb - localRippleDb) / 20.0);
        const double g0 = 1.0;

        double eps;
        if (Math.Abs(gb - g0) > double.Epsilon) {
            eps = Math.Sqrt(((g * g) - (gb * gb)) / ((gb * gb) - (g0 * g0)));
        } else {
            eps = g - 1.0;
        }

        double b = Math.Pow((g / eps) + (gb * Math.Sqrt(1.0 + (1.0 / (eps * eps)))), 1.0 / numPoles);
        double u = Math.Log(b / Math.Pow(g0, 1.0 / numPoles));
        double v = Math.Log(Math.Pow((1.0 / eps) + Math.Sqrt(1.0 + (1.0 / (eps * eps))), 1.0 / numPoles));

        double sinhU = Math.Sinh(u);
        double sinhV = Math.Sinh(v);
        double coshU = Math.Cosh(u);
        double coshV = Math.Cosh(v);

        int n2 = 2 * numPoles;
        int pairs = numPoles / 2;
        for (int i = 1; i <= pairs; i++) {
            double angle = MathEx.DoublePi * ((2 * i) - 1) / n2;
            double sn = Math.Sin(angle);
            double cs = Math.Cos(angle);
            AddPoleZeroConjugatePairs(new Complex(-sn * sinhU, cs * coshU), new Complex(-sn * sinhV, cs * coshV));
        }

        if ((numPoles & 1) != 0) {
            Add(new Complex(-sinhU, 0.0), new Complex(-sinhV, 0.0));
        }
    }
}