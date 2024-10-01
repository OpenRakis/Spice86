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
 * Transforms from an analogue bandpass filter to a digital bandstop filter
 */
public class BandPassTransform {
    private readonly double _wc2;
    private readonly double _wc;
    private readonly double _a, _b;
    private readonly double _a2, _b2;
    private readonly double _ab, _ab2;

    public BandPassTransform(double fc, double fw, LayoutBase digital,
            LayoutBase analog) {

        digital.Reset();

        if (fc < 0) {
            throw new ArithmeticException("Cutoff frequency cannot be negative.");
        }

        if (!(fc < 0.5)) {
            throw new ArithmeticException("Cutoff frequency must be less than the Nyquist frequency.");
        }

        double ww = 2 * Math.PI * fw;

        // pre-calcs
        _wc2 = (2 * Math.PI * fc) - ww / 2;
        _wc = _wc2 + ww;

        // what is this crap?
        if (_wc2 < 1e-8) {
            _wc2 = 1e-8;
        }

        if (_wc > Math.PI - 1e-8) {
            _wc = Math.PI - 1e-8;
        }

        _a = Math.Cos((_wc + _wc2) * 0.5) / Math.Cos((_wc - _wc2) * 0.5);
        _b = 1 / Math.Tan((_wc - _wc2) * 0.5);
        _a2 = _a * _a;
        _b2 = _b * _b;
        _ab = _a * _b;
        _ab2 = 2 * _ab;

        int numPoles = analog.NumPoles;
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; ++i) {
            PoleZeroPair pair = analog.GetPair(i);
            ComplexPair p1 = Transform(pair.poles.First);
            ComplexPair z1 = Transform(pair.zeros.First);

            digital.AddPoleZeroConjugatePairs(p1.First, z1.First);
            digital.AddPoleZeroConjugatePairs(p1.Second, z1.Second);
        }

        if ((numPoles & 1) == 1) {
            ComplexPair poles = Transform(analog.GetPair(pairs).poles.First);
            ComplexPair zeros = Transform(analog.GetPair(pairs).zeros.First);

            digital.Add(poles, zeros);
        }

        double wn = analog.NormalW;
        digital.SetNormal(
                2 * Math.Atan(Math.Sqrt(Math.Tan((_wc + wn) * 0.5)
                        * Math.Tan((_wc2 + wn) * 0.5))), analog.NormalGain);
    }

    private ComplexPair Transform(Complex c) {
        if (Complex.IsInfinity(c)) {
            return new ComplexPair(new Complex(-1, 0), new Complex(1, 0));
        }

        c = new Complex(1, 0).Add(c).Divide(new Complex(1, 0).Subtract(c)); // bilinear

        var v = new Complex(0, 0);
        v = MathSupplement.AddMul(v, 4 * (_b2 * (_a2 - 1) + 1), c);
        v = v.Add(8 * (_b2 * (_a2 - 1) - 1));
        v = v.Multiply(c);
        v = v.Add(4 * (_b2 * (_a2 - 1) + 1));
        v = v.Sqrt();

        Complex u = v.Multiply(-1);
        u = MathSupplement.AddMul(u, _ab2, c);
        u = u.Add(_ab2);

        v = MathSupplement.AddMul(v, _ab2, c);
        v = v.Add(_ab2);

        var d = new Complex(0, 0);
        d = MathSupplement.AddMul(d, 2 * (_b - 1), c).Add(2 * (1 + _b));

        return new ComplexPair(u.Divide(d), v.Divide(d));
    }
}
