namespace Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;

using System.Numerics;

public class LayoutBase {
    private int _maxPoles;
    private double _normalGain = 1.0;
    private double _normalW;
    private int _numPoles;
    private PoleZeroPair[] _pairs = [];

    internal LayoutBase() {
    }

    internal LayoutBase(int maxPoles, PoleZeroPair[] pairs) {
        _maxPoles = maxPoles;
        _pairs = pairs;
    }

    internal ref readonly PoleZeroPair this[int pairIndex] => ref GetPair(pairIndex);

    internal void SetStorage(LayoutBase other) {
        _numPoles = 0;
        _maxPoles = other._maxPoles;
        _pairs = other._pairs;
    }

    internal void Reset() {
        _numPoles = 0;
    }

    internal int GetNumPoles() {
        return _numPoles;
    }

    internal int GetMaxPoles() {
        return _maxPoles;
    }

    internal void Add(Complex pole, Complex zero) {
        if ((_numPoles & 1) != 0) {
            throw new ArgumentException("Can't add 2nd order after a 1st order filter.");
        }

        if (MathEx.IsNaN(pole)) {
            throw new ArgumentException("Pole to add is NaN.");
        }

        if (MathEx.IsNaN(zero)) {
            throw new ArgumentException("Zero to add is NaN.");
        }

        _pairs[_numPoles / 2] = new PoleZeroPair(pole, zero);
        ++_numPoles;
    }

    internal void AddPoleZeroConjugatePairs(Complex pole, Complex zero) {
        if ((_numPoles & 1) != 0) {
            throw new ArgumentException("Can't add 2nd order after a 1st order filter.");
        }

        if (MathEx.IsNaN(pole)) {
            throw new ArgumentException("Pole to add is NaN.");
        }

        if (MathEx.IsNaN(zero)) {
            throw new ArgumentException("Zero to add is NaN.");
        }

        _pairs[_numPoles / 2] = new PoleZeroPair(
            pole,
            zero,
            Complex.Conjugate(pole),
            Complex.Conjugate(zero));
        _numPoles += 2;
    }

    internal void Add(ComplexPair poles, ComplexPair zeros) {
        if ((_numPoles & 1) != 0) {
            throw new ArgumentException("Can't add 2nd order after a 1st order filter.");
        }

        if (!poles.IsMatchedPair()) {
            throw new ArgumentException("Poles not complex conjugate.");
        }

        if (!zeros.IsMatchedPair()) {
            throw new ArgumentException("Zeros not complex conjugate.");
        }

        _pairs[_numPoles / 2] = new PoleZeroPair(poles.First, zeros.First, poles.Second, zeros.Second);
        _numPoles += 2;
    }

    internal ref readonly PoleZeroPair GetPair(int pairIndex) {
        if (pairIndex < 0 || pairIndex >= (_numPoles + 1) / 2) {
            throw new ArgumentOutOfRangeException(nameof(pairIndex), "Pair index out of bounds.");
        }

        return ref _pairs[pairIndex];
    }

    internal double GetNormalW() {
        return _normalW;
    }

    internal double GetNormalGain() {
        return _normalGain;
    }

    internal void SetNormal(double w, double g) {
        _normalW = w;
        _normalGain = g;
    }
}

internal sealed class LayoutStorage {
    internal LayoutStorage(int maxPoles) {
        var pairs = new PoleZeroPair[(maxPoles + 1) / 2];
        Base = new LayoutBase(maxPoles, pairs);
    }

    internal LayoutBase Base { get; }
}