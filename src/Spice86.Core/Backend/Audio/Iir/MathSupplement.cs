/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 *  Copyright (c) 2009 by Vinnie Falco
 *  Copyright (c) 2016 by Bernd Porr
 */

using System.Numerics;

namespace Spice86.Core.Backend.Audio.Iir;

/**
 *
 * Useful math functions which come back over and over again
 *
 */
public static class MathSupplement {
    public const double DoublePi = 3.1415926535897932384626433832795028841971;
    public const double DoublePi2 = 1.5707963267948966192313216916397514420986;
    public const double DoubleLn2 = 0.69314718055994530941723212145818;
    public const double DoubleLn10 = 2.3025850929940456840179914546844;

    public static Complex SolveQuadratic1(double a, double b, double c) {
        return new Complex(-b, 0).Add(new Complex(b * b - 4 * a * c, 0)).Sqrt()
                .Divide(2.0 * a);
    }

    public static Complex SolveQuadratic2(double a, double b, double c) {
        return new Complex(-b, 0).Subtract(new Complex(b * b - 4 * a * c, 0))
                .Sqrt().Divide(2.0 * a);
    }

    public static Complex AdjustImage(Complex c) {
        if (Math.Abs(c.Imaginary) < 1e-30) {
            return new Complex(c.Real, 0);
        } else {
            return c;
        }
    }

    public static Complex AddMul(Complex c, double v, Complex c1) {
        return new Complex(c.Real + v * c1.Real, c.Imaginary + v
                * c1.Imaginary);
    }

    public static Complex Recip(Complex c) {
        double n = 1.0 / (Complex.Abs(c) * Complex.Abs(c));

        return new Complex(n * c.Real, n * c.Imaginary);
    }

    public static double Asinh(double x) {
        return Math.Log(x + Math.Sqrt(x * x + 1));
    }

    public static double Acosh(double x) {
        return Math.Log(x + Math.Sqrt(x * x - 1));
    }
}
