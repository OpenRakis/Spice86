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
 * Contains the coefficients of a 2nd order digital filter with two poles and two zeros
 */
public class Biquad {
    public double A0 { get; set; }
    public double A1 { get; set; }
    public double A2 { get; set; }
    public double B1 { get; set; }
    public double B2 { get; set; }
    public double B0 { get; set; }

    public double GetA0 => A0;

    public double GetA1 => A1 * A0;

    public double GetA2 => A2 * A0;

    public double GetB0 => B0 * A0;

    public double GetB1 => B1 * A0;

    public double GetB2 => B2 * A0;

    public Complex Response(double normalizedFrequency) {
        double a0 = GetA0;
        double a1 = GetA1;
        double a2 = GetA2;
        double b0 = GetB0;
        double b1 = GetB1;
        double b2 = GetB2;

        double w = 2 * Math.PI * normalizedFrequency;
        Complex czn1 = ComplexUtils.PolarToComplex(1.0, -w);
        Complex czn2 = ComplexUtils.PolarToComplex(1.0, -2 * w);
        var ch = new Complex(1, 0);
        var cbot = new Complex(1, 0);

        var ct = new Complex(b0 / a0, 0);
        var cb = new Complex(1, 0);
        ct = MathSupplement.AddMul(ct, b1 / a0, czn1);
        ct = MathSupplement.AddMul(ct, b2 / a0, czn2);
        cb = MathSupplement.AddMul(cb, a1 / a0, czn1);
        cb = MathSupplement.AddMul(cb, a2 / a0, czn2);
        ch = Complex.Multiply(ch, ct);
        cbot = Complex.Multiply(cbot, cb);

        return Complex.Divide(ch, cbot);
    }

    public void SetCoefficients(double a0, double a1, double a2,
                double b0, double b1, double b2) {
        A0 = a0;
        A1 = a1 / a0;
        A2 = a2 / a0;
        B0 = b0 / a0;
        B1 = b1 / a0;
        B2 = b2 / a0;
    }

    public void SetOnePole(Complex pole, Complex zero) {
        const double a0 = 1;
        double a1 = -pole.Real;
        const double a2 = 0;
        double b0 = -zero.Real;
        const double b1 = 1;
        const double b2 = 0;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void SetTwoPole(
        Complex pole1, Complex zero1,
        Complex pole2, Complex zero2) {
        const double a0 = 1;
        double a1;
        double a2;

        if (pole1.Imaginary != 0) {
            a1 = -2 * pole1.Real;
            a2 = Complex.Abs(pole1) * Complex.Abs(pole1);
        } else {
            a1 = -(pole1.Real + pole2.Real);
            a2 = pole1.Real * pole2.Real;
        }

        const double b0 = 1;
        double b1;
        double b2;

        if (zero1.Imaginary != 0) {
            b1 = -2 * zero1.Real;
            b2 = Complex.Abs(zero1) * Complex.Abs(zero1);
        } else {
            b1 = -(zero1.Real + zero2.Real);
            b2 = zero1.Real * zero2.Real;
        }

        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void SetPoleZeroForm(BiquadPoleState bps) {
        SetPoleZeroPair(bps);
        ApplyScale(bps.Gain);
    }

    public void SetIdentity() {
        SetCoefficients(1, 0, 0, 1, 0, 0);
    }

    public void ApplyScale(double scale) {
        B0 *= scale;
        B1 *= scale;
        B2 *= scale;
    }


    public void SetPoleZeroPair(PoleZeroPair pair) {
        if (pair.IsSinglePole()) {
            SetOnePole(pair.poles.First, pair.zeros.First);
        } else {
            SetTwoPole(pair.poles.First, pair.zeros.First,
                    pair.poles.Second, pair.zeros.Second);
        }
    }
}
