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
 * Transforms from an analogue lowpass filter to a digital bandstop filter
 */
public class BandStopTransform {
    private readonly double _wc;
    private readonly double _wc2;
    private readonly double _a;
    private readonly double _b;
    private readonly double _a2;
    private readonly double _b2;


    public BandStopTransform(double fc,
        double fw,
        LayoutBase digital,
        LayoutBase analog) {
        digital.Reset();

        if (fc < 0) {
            throw new ArithmeticException("Cutoff frequency cannot be negative.");
        }

        if (!(fc < 0.5)) {
            throw new ArithmeticException("Cutoff frequency must be less than the Nyquist frequency.");
        }

        double ww = 2 * Math.PI * fw;

        _wc2 = 2 * Math.PI * fc - ww / 2;
        _wc = _wc2 + ww;

        // this is crap
        if (_wc2 < 1e-8) {
            _wc2 = 1e-8;
        }

        if (_wc > Math.PI - 1e-8) {
            _wc = Math.PI - 1e-8;
        }

        _a = Math.Cos((_wc + _wc2) * .5) /
             Math.Cos((_wc - _wc2) * .5);
        _b = Math.Tan((_wc - _wc2) * .5);
        _a2 = _a * _a;
        _b2 = _b * _b;

        int numPoles = analog.NumPoles;
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; i++) {
            PoleZeroPair pair = analog.GetPair(i);
            ComplexPair p = Transform(pair.poles.First);
            ComplexPair z = Transform(pair.zeros.First);
            digital.AddPoleZeroConjugatePairs(p.First, z.First);
            digital.AddPoleZeroConjugatePairs(p.Second, z.Second);
        }

        if ((numPoles & 1) == 1) {
            ComplexPair poles = Transform(analog.GetPair(pairs).poles.First);
            ComplexPair zeros = Transform(analog.GetPair(pairs).zeros.First);

            digital.Add(poles, zeros);
        }

        if (fc < 0.25) {
            digital.SetNormal(Math.PI, analog.NormalGain);
        } else {
            digital.SetNormal(0, analog.NormalGain);
        }
    }

    private ComplexPair Transform(Complex c) {
        if (c == Complex.Infinity) {
            c = new Complex(-1, 0);
        } else {
            c = new Complex(1, 0).Add(c).Divide(new Complex(1, 0).Subtract(c)); // bilinear
        }

        var u = new Complex(0, 0);
        u = MathSupplement.AddMul(u, 4 * (_b2 + _a2 - 1), c);
        u = u.Add(8 * (_b2 - _a2 + 1));
        u = u.Multiply(c);
        u = u.Add(4 * (_a2 + _b2 - 1));
        u = u.Sqrt();

        Complex v = u.Multiply(-.5);
        v = v.Add(_a);
        v = MathSupplement.AddMul(v, -_a, c);

        u = u.Multiply(.5);
        u = u.Add(_a);
        u = MathSupplement.AddMul(u, -_a, c);

        var d = new Complex(_b + 1, 0);
        d = MathSupplement.AddMul(d, _b - 1, c);

        return new ComplexPair(u.Divide(d), v.Divide(d));
    }
}
