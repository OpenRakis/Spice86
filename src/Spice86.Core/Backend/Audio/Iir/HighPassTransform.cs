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
 * Transforms from an analogue lowpass filter to a digital highpass filter
 */
public class HighPassTransform {

    private readonly double f;

    public HighPassTransform(double fc, LayoutBase digital, LayoutBase analog) {
        digital.Reset();

        if (fc < 0) {
            throw new ArithmeticException("Cutoff frequency cannot be negative.");
        }

        if (!(fc < 0.5)) {
            throw new ArithmeticException("Cutoff frequency must be less than the Nyquist frequency.");
        }

        // prewarp
        f = 1.0d / Math.Tan(Math.PI * fc);

        int numPoles = analog.NumPoles;
        int pairs = numPoles / 2;
        for (int i = 0; i < pairs; ++i) {
            PoleZeroPair pair = analog.GetPair(i);
            digital.AddPoleZeroConjugatePairs(Transform(pair.poles.First),
                    Transform(pair.zeros.First));
        }

        if ((numPoles & 1) == 1) {
            PoleZeroPair pair = analog.GetPair(pairs);
            digital.Add(Transform(pair.poles.First),
                    Transform(pair.zeros.First));
        }

        digital.SetNormal(Math.PI - analog.NormalW, analog.NormalGain);
    }

    private Complex Transform(Complex c) {
        if (Complex.IsInfinity(c)) {
            return new Complex(1, 0);
        }

        // frequency transform
        c = Complex.Multiply(c, f);

        // bilinear high pass transform
        return new Complex(-1, 0).Multiply(new Complex(1, 0).Add(c)).Divide(
                new Complex(1, 0).Subtract(c));
    }
}
