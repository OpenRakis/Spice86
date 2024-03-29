namespace Spice86.Core.Backend.Audio.IirFilters;

using System.Numerics;

/// <summary>
/// Extensions added to <see cref="Complex"/> so the porting from Java was easier.
/// </summary>
internal static class ComplexExtensions {
    public static Complex Add(this Complex a, Complex b) => Complex.Add(a, b);
    public static Complex Multiply(this Complex a, Complex b) => Complex.Multiply(a, b);
    public static Complex Divide(this Complex a, Complex b) => Complex.Divide(a, b);
    public static Complex Subtract(this Complex a, Complex b) => Complex.Subtract(a, b);
    public static Complex Sqrt(this Complex a) => Complex.Sqrt(a);
}