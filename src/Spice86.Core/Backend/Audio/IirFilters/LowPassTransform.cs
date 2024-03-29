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

namespace Spice86.Core.Backend.Audio.IirFilters;

using System.Numerics;

/**
 * Transforms from an analogue lowpass filter to a digital lowpass filter
 */
internal class LowPassTransform {
    private readonly double _f;

    private Complex Transform(Complex c) {
        if (Complex.IsInfinity(c)) {
            return new Complex(-1, 0);
        }

        // frequency transform
        c = Complex.Multiply(c, _f);

        var one = new Complex(1, 0);

        // bilinear low pass transform
        return Complex.Divide(Complex.Add(c, one), Complex.Subtract(c, one));
    }

    public LowPassTransform(double fc, LayoutBase digital, LayoutBase analog) {
        digital.Reset();

        if (fc < 0) {
            throw new ArithmeticException("Cutoff frequency cannot be negative.");
        }

        if (fc > 0.5) {
            throw new ArithmeticException("Cutoff frequency must be less than the Nyquist frequency.");
        }

        // prewarp
        _f = Math.Tan(Math.PI * fc);

        int numPoles = analog.NumPoles;
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; ++i) {
            PoleZeroPair pair = analog.GetPair(i);
            digital.AddPoleZeroConjugatePairs(Transform(pair.Poles.First),
                    Transform(pair.Zeros.First));
        }

        if ((numPoles & 1) == 1) {
            PoleZeroPair pair = analog.GetPair(pairs);
            digital.Add(Transform(pair.Poles.First),
                    Transform(pair.Zeros.First));
        }

        digital.SetNormal(analog.NormalW, analog.NormalGain);
    }
}
