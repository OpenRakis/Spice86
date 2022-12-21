using System.Numerics;

namespace Spice86.Core.Backend.Audio.Iir;

public static class ComplexUtils {
    public static Complex PolarToComplex(double r, double theta) {
        if (r < 0.0) {
            throw new ArgumentException(r.ToString(), nameof(r));
        } else {
            return new Complex(r * Math.Cos(theta), r * Math.Sin(theta));
        }
    }

    public static Complex[] ConvertToComplex(double[] real) {
        var c = new Complex[real.Length];

        for (int i = 0; i < real.Length; ++i) {
            c[i] = new Complex(real[i], 0.0);
        }

        return c;
    }
}
