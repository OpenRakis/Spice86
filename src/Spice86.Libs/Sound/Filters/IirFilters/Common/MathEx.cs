namespace Spice86.Libs.Sound.Filters.IirFilters.Common;

using System.Numerics;

/// <summary>
/// Extended mathematical functions and constants for filter calculations.
/// </summary>
internal static class MathEx {
    /// <summary>
    /// High-precision value of π (pi).
    /// </summary>
    internal const double DoublePi = 3.1415926535897932384626433832795028841971;
    
    /// <summary>
    /// High-precision value of π/2 (pi over two).
    /// </summary>
    internal const double DoublePiOverTwo = 1.5707963267948966192313216916397514420986;
    
    /// <summary>
    /// High-precision value of ln(2) (natural logarithm of 2).
    /// </summary>
    internal const double DoubleLn2 = 0.69314718055994530941723212145818;
    
    /// <summary>
    /// High-precision value of ln(10) (natural logarithm of 10).
    /// </summary>
    internal const double DoubleLn10 = 2.3025850929940456840179914546844;

    /// <summary>
    /// Returns a complex number representing positive infinity.
    /// </summary>
    /// <returns>A complex number with positive infinity real part and zero imaginary part.</returns>
    internal static Complex Infinity() {
        return new Complex(double.PositiveInfinity, 0.0);
    }

    /// <summary>
    /// Adds a complex number to the product of a scalar and another complex number.
    /// </summary>
    /// <param name="c">The complex number to add to.</param>
    /// <param name="v">The scalar multiplier.</param>
    /// <param name="c1">The complex number to multiply.</param>
    /// <returns>The result of c + (v * c1).</returns>
    internal static Complex AddMul(Complex c, double v, Complex c1) {
        return new Complex(c.Real + (v * c1.Real), c.Imaginary + (v * c1.Imaginary));
    }

    /// <summary>
    /// Calculates the inverse hyperbolic sine of a value.
    /// </summary>
    /// <param name="x">The value.</param>
    /// <returns>The inverse hyperbolic sine of x.</returns>
    internal static double Asinh(double x) {
        return Math.Log(x + Math.Sqrt((x * x) + 1.0));
    }

    /// <summary>
    /// Determines whether a double value is NaN (Not a Number).
    /// </summary>
    /// <param name="v">The value to check.</param>
    /// <returns><c>true</c> if the value is NaN; otherwise, <c>false</c>.</returns>
    internal static bool IsNaN(double v) {
        return double.IsNaN(v);
    }

    /// <summary>
    /// Determines whether a complex value has a NaN (Not a Number) component.
    /// </summary>
    /// <param name="v">The complex value to check.</param>
    /// <returns><c>true</c> if either the real or imaginary part is NaN; otherwise, <c>false</c>.</returns>
    internal static bool IsNaN(Complex v) {
        return IsNaN(v.Real) || IsNaN(v.Imaginary);
    }

    /// <summary>
    /// Determines whether a complex value has an infinite component.
    /// </summary>
    /// <param name="v">The complex value to check.</param>
    /// <returns><c>true</c> if either the real or imaginary part is infinite; otherwise, <c>false</c>.</returns>
    internal static bool IsInfinity(Complex v) {
        return double.IsInfinity(v.Real) || double.IsInfinity(v.Imaginary);
    }
}