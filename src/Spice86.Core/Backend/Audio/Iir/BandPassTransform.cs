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

    private double wc2;
    private double wc;
    private double a, b;
    private double a2, b2;
    private double ab, ab_2;

    public BandPassTransform(double fc, double fw, LayoutBase digital,
            LayoutBase analog) {

        digital.reset();

        if (fc < 0) {
            throw new ArithmeticException("Cutoff frequency cannot be negative.");
        }

        if (!(fc < 0.5)) {
            throw new ArithmeticException("Cutoff frequency must be less than the Nyquist frequency.");
        }

        double ww = 2 * Math.PI * fw;

        // pre-calcs
        wc2 = 2 * Math.PI * fc - ww / 2;
        wc = wc2 + ww;

        // what is this crap?
        if (wc2 < 1e-8)
            wc2 = 1e-8;
        if (wc > Math.PI - 1e-8)
            wc = Math.PI - 1e-8;

        a = Math.Cos((wc + wc2) * 0.5) / Math.Cos((wc - wc2) * 0.5);
        b = 1 / Math.Tan((wc - wc2) * 0.5);
        a2 = a * a;
        b2 = b * b;
        ab = a * b;
        ab_2 = 2 * ab;

        int numPoles = analog.getNumPoles();
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; ++i) {
            PoleZeroPair pair = analog.getPair(i);
            ComplexPair p1 = transform(pair.poles.first);
            ComplexPair z1 = transform(pair.zeros.first);

            digital.addPoleZeroConjugatePairs(p1.first, z1.first);
            digital.addPoleZeroConjugatePairs(p1.second, z1.second);
        }

        if ((numPoles & 1) == 1) {
            ComplexPair poles = transform(analog.getPair(pairs).poles.first);
            ComplexPair zeros = transform(analog.getPair(pairs).zeros.first);

            digital.add(poles, zeros);
        }

        double wn = analog.getNormalW();
        digital.setNormal(
                2 * Math.Atan(Math.Sqrt(Math.Tan((wc + wn) * 0.5)
                        * Math.Tan((wc2 + wn) * 0.5))), analog.getNormalGain());
    }

    private ComplexPair transform(Complex c) {
        if (Complex.IsInfinity(c)) {
            return new ComplexPair(new Complex(-1, 0), new Complex(1, 0));
        }

        c = new Complex(1, 0).Add(c).Divide(new Complex(1, 0).Subtract(c)); // bilinear

        var v = new Complex(0, 0);
        v = MathSupplement.addmul(v, 4 * (b2 * (a2 - 1) + 1), c);
        v = v.Add(8 * (b2 * (a2 - 1) - 1));
        v = v.Multiply(c);
        v = v.Add(4 * (b2 * (a2 - 1) + 1));
        v = v.Sqrt();

        Complex u = v.Multiply(-1);
        u = MathSupplement.addmul(u, ab_2, c);
        u = u.Add(ab_2);

        v = MathSupplement.addmul(v, ab_2, c);
        v = v.Add(ab_2);

        var d = new Complex(0, 0);
        d = MathSupplement.addmul(d, 2 * (b - 1), c).Add(2 * (1 + b));

        return new ComplexPair(u.Divide(d), v.Divide(d));
    }
}
