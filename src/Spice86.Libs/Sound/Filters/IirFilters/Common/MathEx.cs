namespace Spice86.Libs.Sound.Filters.IirFilters.Common;

using System.Numerics;

internal static class MathEx {
    internal const double DoublePi = 3.1415926535897932384626433832795028841971;
    internal const double DoublePiOverTwo = 1.5707963267948966192313216916397514420986;
    internal const double DoubleLn2 = 0.69314718055994530941723212145818;
    internal const double DoubleLn10 = 2.3025850929940456840179914546844;

    internal static Complex Infinity() {
        return new Complex(double.PositiveInfinity, 0.0);
    }

    internal static Complex AddMul(Complex c, double v, Complex c1) {
        return new Complex(c.Real + (v * c1.Real), c.Imaginary + (v * c1.Imaginary));
    }

    internal static double Asinh(double x) {
        return Math.Log(x + Math.Sqrt((x * x) + 1.0));
    }

    internal static bool IsNaN(double v) {
        return double.IsNaN(v);
    }

    internal static bool IsNaN(Complex v) {
        return IsNaN(v.Real) || IsNaN(v.Imaginary);
    }

    internal static bool IsInfinity(Complex v) {
        return double.IsInfinity(v.Real) || double.IsInfinity(v.Imaginary);
    }
}