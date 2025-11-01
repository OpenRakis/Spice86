namespace Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth;

using Spice86.Libs.Sound.Filters.IirFilters.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;

using System.Numerics;

public sealed class AnalogLowPass : LayoutBase {
    private int _numPoles = -1;

    public AnalogLowPass() {
        SetNormal(0.0, 1.0);
    }

    public void Design(int numPoles) {
        if (_numPoles == numPoles) {
            return;
        }

        _numPoles = numPoles;
        Reset();

        int n2 = 2 * numPoles;
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; i++) {
            double angle = MathEx.DoublePiOverTwo + (((2 * i) + 1) * MathEx.DoublePi / n2);
            var pole = Complex.FromPolarCoordinates(1.0, angle);
            AddPoleZeroConjugatePairs(pole, MathEx.Infinity());
        }

        if ((numPoles & 1) != 0) {
            Add(-1.0, MathEx.Infinity());
        }
    }
}

public sealed class AnalogLowShelf : LayoutBase {
    private double _gainDb;
    private int _numPoles = -1;

    public AnalogLowShelf() {
        SetNormal(MathEx.DoublePi, 1.0);
    }

    public void Design(int numPoles, double gainDb) {
        if (_numPoles == numPoles && Math.Abs(_gainDb - gainDb) <= double.Epsilon) {
            return;
        }

        _numPoles = numPoles;
        _gainDb = gainDb;

        Reset();

        int n2 = numPoles * 2;
        double g = Math.Pow(Math.Pow(10.0, gainDb / 20.0), 1.0 / n2);
        double gp = -1.0 / g;
        double gz = -g;

        int pairs = numPoles / 2;
        for (int i = 1; i <= pairs; i++) {
            double theta = MathEx.DoublePi * (0.5 - (((2.0 * i) - 1.0) / n2));
            AddPoleZeroConjugatePairs(Complex.FromPolarCoordinates(gp, theta), Complex.FromPolarCoordinates(gz, theta));
        }

        if ((numPoles & 1) != 0) {
            Add(gp, gz);
        }
    }
}