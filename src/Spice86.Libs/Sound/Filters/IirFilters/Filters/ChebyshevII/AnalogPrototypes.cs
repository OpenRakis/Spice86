namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.ChebyshevII;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;

using System.Numerics;

public sealed class AnalogLowPass : LayoutBase {
    private int _numPoles = -1;
    private double _stopBandDb;

    public AnalogLowPass() {
        SetNormal(0.0, 1.0);
    }

    public void Design(int numPoles, double stopBandDb) {
        if (_numPoles == numPoles && Math.Abs(_stopBandDb - stopBandDb) <= double.Epsilon) {
            return;
        }

        _numPoles = numPoles;
        _stopBandDb = stopBandDb;

        Reset();

        if (numPoles == 0) {
            return;
        }

        double eps = Math.Sqrt(1.0 / (Math.Exp(stopBandDb * 0.1 * MathEx.DoubleLn10) - 1.0));
        double v0 = MathEx.Asinh(1.0 / eps) / numPoles;
        double sinhV0 = -Math.Sinh(v0);
        double coshV0 = Math.Cosh(v0);
        double fn = MathEx.DoublePi / (2.0 * numPoles);

        int k = 1;
        for (int i = numPoles / 2; i > 0; i--, k += 2) {
            double a = sinhV0 * Math.Cos((k - numPoles) * fn);
            double b = coshV0 * Math.Sin((k - numPoles) * fn);
            double d2 = (a * a) + (b * b);
            double im = 1.0 / Math.Cos(k * fn);
            var pole = new Complex(a / d2, b / d2);
            var zero = new Complex(0.0, im);
            AddPoleZeroConjugatePairs(pole, zero);
        }

        if ((numPoles & 1) != 0) {
            Add(new Complex(1.0 / sinhV0, 0.0), MathEx.Infinity());
        }
    }
}

public sealed class AnalogLowShelf : LayoutBase {
    private double _gainDb;
    private int _numPoles = -1;
    private double _stopBandDb;

    public AnalogLowShelf() {
        SetNormal(MathEx.DoublePi, 1.0);
    }

    public void Design(int numPoles, double gainDb, double stopBandDb) {
        if (_numPoles == numPoles &&
            Math.Abs(_gainDb - gainDb) <= double.Epsilon &&
            Math.Abs(_stopBandDb - stopBandDb) <= double.Epsilon) {
            return;
        }

        _numPoles = numPoles;
        _gainDb = gainDb;
        _stopBandDb = stopBandDb;

        Reset();

        if (numPoles == 0) {
            return;
        }

        double localGainDb = -gainDb;
        double localStopBandDb = stopBandDb;

        if (localStopBandDb >= Math.Abs(localGainDb)) {
            localStopBandDb = Math.Abs(localGainDb);
        }

        if (localGainDb < 0.0) {
            localStopBandDb = -localStopBandDb;
        }

        double g = Math.Pow(10.0, localGainDb / 20.0);
        double gb = Math.Pow(10.0, (localGainDb - localStopBandDb) / 20.0);
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