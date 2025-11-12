namespace Spice86.Libs.Sound.Filters.IirFilters.Common.Layout;

using System.Numerics;

public struct ComplexPair {
    internal Complex First;
    internal Complex Second;

    internal ComplexPair(Complex first, Complex second) {
        First = first;
        Second = second;
    }

    internal ComplexPair(Complex single) {
        double imag = Math.Abs(single.Imaginary);
        if (imag < 1e-12) {
            single = new Complex(single.Real, 0.0);
        }

        if (single.Imaginary != 0.0) {
            throw new ArgumentException("A single complex number needs to be real.", nameof(single));
        }

        First = single;
        Second = Complex.Zero;
    }

    internal bool IsReal() {
        return Math.Abs(First.Imaginary) < 1e-12 && Math.Abs(Second.Imaginary) < 1e-12;
    }

    internal bool IsMatchedPair() {
        if (First.Imaginary != 0.0) {
            return Second == Complex.Conjugate(First);
        }

        return Math.Abs(Second.Imaginary) < 1e-12 &&
               Second.Real != 0.0 &&
               First.Real != 0.0;
    }

    internal readonly bool IsNaN() {
        return MathEx.IsNaN(First) || MathEx.IsNaN(Second);
    }
}

public struct PoleZeroPair {
    internal ComplexPair Poles;
    internal ComplexPair Zeros;

    internal PoleZeroPair(ComplexPair poles, ComplexPair zeros) {
        Poles = poles;
        Zeros = zeros;
    }

    internal PoleZeroPair(Complex pole, Complex zero) {
        Poles = new ComplexPair(pole);
        Zeros = new ComplexPair(zero);
    }

    internal PoleZeroPair(Complex p1, Complex z1, Complex p2, Complex z2) {
        Poles = new ComplexPair(p1, p2);
        Zeros = new ComplexPair(z1, z2);
    }

    internal readonly bool IsSinglePole() {
        return Poles.Second == Complex.Zero && Zeros.Second == Complex.Zero;
    }

    internal readonly bool IsNaN() {
        return Poles.IsNaN() || Zeros.IsNaN();
    }
}