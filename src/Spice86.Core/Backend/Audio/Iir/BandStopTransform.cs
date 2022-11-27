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


    private double wc;
    private double wc2;
    private double a;
    private double b;
    private double a2;
    private double b2;


    public BandStopTransform(double fc,
                 double fw,
                 LayoutBase digital,
                 LayoutBase analog) {
        digital.reset();

        if (fc < 0) {
            throw new ArithmeticException("Cutoff frequency cannot be negative.");
        }

        if (!(fc < 0.5)) {
            throw new ArithmeticException("Cutoff frequency must be less than the Nyquist frequency.");
        }

        double ww = 2 * Math.PI * fw;

        wc2 = 2 * Math.PI * fc - ww / 2;
        wc = wc2 + ww;

        // this is crap
        if (wc2 < 1e-8)
            wc2 = 1e-8;
        if (wc > Math.PI - 1e-8)
            wc = Math.PI - 1e-8;

        a = Math.Cos((wc + wc2) * .5) /
                Math.Cos((wc - wc2) * .5);
        b = Math.Tan((wc - wc2) * .5);
        a2 = a * a;
        b2 = b * b;

        int numPoles = analog.getNumPoles();
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; i++) {
            PoleZeroPair pair = analog.getPair(i);
            ComplexPair p = transform(pair.poles.first);
            ComplexPair z = transform(pair.zeros.first);
            digital.addPoleZeroConjugatePairs(p.first, z.first);
            digital.addPoleZeroConjugatePairs(p.second, z.second);
        }

        if ((numPoles & 1) == 1) {
            ComplexPair poles = transform(analog.getPair(pairs).poles.first);
            ComplexPair zeros = transform(analog.getPair(pairs).zeros.first);

            digital.add(poles, zeros);
        }

        if (fc < 0.25)
            digital.setNormal(Math.PI, analog.getNormalGain());
        else
            digital.setNormal(0, analog.getNormalGain());
    }

    private ComplexPair transform(Complex c) {
        if (c == Complex.Infinity)
            c = new Complex(-1, 0);
        else
            c = new Complex(1, 0).Add(c).Divide(new Complex(1, 0).Subtract(c)); // bilinear

        var u = new Complex(0, 0);
        u = MathSupplement.addmul(u, 4 * (b2 + a2 - 1), c);
        u = u.Add(8 * (b2 - a2 + 1));
        u = u.Multiply(c);
        u = u.Add(4 * (a2 + b2 - 1));
        u = u.Sqrt();

        Complex v = u.Multiply(-.5);
        v = v.Add(a);
        v = MathSupplement.addmul(v, -a, c);

        u = u.Multiply(.5);
        u = u.Add(a);
        u = MathSupplement.addmul(u, -a, c);

        var d = new Complex(b + 1, 0);
        d = MathSupplement.addmul(d, b - 1, c);

        return new ComplexPair(u.Divide(d), v.Divide(d));
    }

}
