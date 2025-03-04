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
namespace Spice86.Core.Backend.Audio.Iir;

/**
 * Filter realizations based on Robert Bristol-Johnson formulae:
 *
 * http://www.musicdsp.org/files/Audio-EQ-Cookbook.txt
 *
 * These are all 2nd order filters which are tuned with the Q (or Quality factor).
 * The Q factor causes a resonance at the cutoff frequency. The higher the Q
 * factor the higher the responance. If 0.5 < Q < 1/sqrt(2) then there is no resonance peak.
 * Above 1/sqrt(2) the peak becomes more and more pronounced. For bandpass and stopband
 * the Q factor is replaced by the width of the filter. The higher Q the more narrow
 * the bandwidth of the notch or bandpass.
 *
 **/

public class RBJBase : Biquad {
    private readonly DirectFormI _state = new();

    public double Filter(double s) {
        return _state.Process1(s, this);
    }

    public DirectFormI GetState() {
        return _state;
    }
}

public class LowPass : RBJBase {
    private const double ONESQRT2 = 0.707106781;

    public void SetupN(
        double cutoffFrequency,
        double q = ONESQRT2) {
        double w0 = 2 * MathSupplement.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double AL = sn / (2 * q);
        double b0 = (1 - cs) / 2;
        double b1 = 1 - cs;
        double b2 = (1 - cs) / 2;
        double a0 = 1 + AL;
        double a1 = -2 * cs;
        double a2 = 1 - AL;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(
        double sampleRate,
        double cutoffFrequency,
        double q = ONESQRT2) {
        SetupN(cutoffFrequency / sampleRate, q);
    }
}

public class HighPass : RBJBase {
    private const double ONESQRT2 = 0.707106781;

    public void SetupN(
        double cutoffFrequency,
        double q = ONESQRT2) {
        double w0 = 2 * MathSupplement.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double AL = sn / (2 * q);
        double b0 = (1 + cs) / 2;
        double b1 = -(1 + cs);
        double b2 = (1 + cs) / 2;
        double a0 = 1 + AL;
        double a1 = -2 * cs;
        double a2 = 1 - AL;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(
        double sampleRate,
        double cutoffFrequency,
        double q = ONESQRT2) {
        SetupN(cutoffFrequency / sampleRate, q);
    }
}

public class BandPass1 : RBJBase {
    public void SetupN(
        double centerFrequency,
        double bandWidth) {
        double w0 = 2 * MathSupplement.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double AL = sn / (2 * bandWidth);
        double b0 = bandWidth * AL;// sn / 2;
        double b1 = 0;
        double b2 = -bandWidth * AL;//-sn / 2;
        double a0 = 1 + AL;
        double a1 = -2 * cs;
        double a2 = 1 - AL;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(
        double sampleRate,
        double centerFrequency,
        double bandWidth) {
        SetupN(centerFrequency / sampleRate, bandWidth);
    }
}

public class BandPass2 : RBJBase {
    public void SetupN(
        double centerFrequency,
        double bandWidth) {
        double w0 = 2 * MathSupplement.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double AL = sn / (2 * bandWidth);
        double b0 = AL;
        double b1 = 0;
        double b2 = -AL;
        double a0 = 1 + AL;
        double a1 = -2 * cs;
        double a2 = 1 - AL;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(
        double sampleRate,
        double centerFrequency,
        double bandWidth) {
        SetupN(centerFrequency / sampleRate, bandWidth);
    }
}

public class BandStop : RBJBase {
    public void SetupN(
        double centerFrequency,
        double bandWidth) {
        double w0 = 2 * MathSupplement.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double AL = sn / (2 * bandWidth);
        double b0 = 1;
        double b1 = -2 * cs;
        double b2 = 1;
        double a0 = 1 + AL;
        double a1 = -2 * cs;
        double a2 = 1 - AL;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(
        double sampleRate,
        double centerFrequency,
        double bandWidth) {
        SetupN(centerFrequency / sampleRate, bandWidth);
    }
}

public class IIRNotch : RBJBase {
    public void SetupN(
        double centerFrequency,
        double q_factor = 10) {
        double w0 = 2 * MathSupplement.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double r = Math.Exp(-(w0 / 2) / q_factor);
        const double b0 = 1;
        double b1 = -2 * cs;
        const double b2 = 1;
        const double a0 = 1;
        double a1 = -2 * r * cs;
        double a2 = r * r;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(
        double sampleRate,
        double centerFrequency,
        double q_factor = 10) {
        SetupN(centerFrequency / sampleRate, q_factor);
    }
}


public class LowShelf : RBJBase {
    public void SetupN(
        double cutoffFrequency,
        double gainDb,
        double shelfSlope = 1) {
        double A = Math.Pow(10, gainDb / 40);
        double w0 = 2 * MathSupplement.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double AL = sn / 2 * Math.Sqrt((A + 1 / A) * (1 / shelfSlope - 1) + 2);
        double sq = 2 * Math.Sqrt(A) * AL;
        double b0 = A * (A + 1 - (A - 1) * cs + sq);
        double b1 = 2 * A * (A - 1 - (A + 1) * cs);
        double b2 = A * (A + 1 - (A - 1) * cs - sq);
        double a0 = A + 1 + (A - 1) * cs + sq;
        double a1 = -2 * (A - 1 + (A + 1) * cs);
        double a2 = A + 1 + (A - 1) * cs - sq;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(double sampleRate,
        double cutoffFrequency,
        double gainDb,
        double shelfSlope = 1) {
        SetupN(cutoffFrequency / sampleRate, gainDb, shelfSlope);
    }
}


public class HighShelf : RBJBase {
    public void SetupN(
        double cutoffFrequency,
        double gainDb,
        double shelfSlope = 1) {
        double A = Math.Pow(10, gainDb / 40);
        double w0 = 2 * MathSupplement.DoublePi * cutoffFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double AL = sn / 2 * Math.Sqrt((A + 1 / A) * (1 / shelfSlope - 1) + 2);
        double sq = 2 * Math.Sqrt(A) * AL;
        double b0 = A * (A + 1 + (A - 1) * cs + sq);
        double b1 = -2 * A * (A - 1 + (A + 1) * cs);
        double b2 = A * (A + 1 + (A - 1) * cs - sq);
        double a0 = A + 1 - (A - 1) * cs + sq;
        double a1 = 2 * (A - 1 - (A + 1) * cs);
        double a2 = A + 1 - (A - 1) * cs - sq;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(double sampleRate,
        double cutoffFrequency,
        double gainDb,
        double shelfSlope = 1) {
        SetupN(cutoffFrequency / sampleRate, gainDb, shelfSlope);
    }
}

public class BandShelf : RBJBase {
    public void SetupN(
        double centerFrequency,
        double gainDb,
        double bandWidth) {
        double A = Math.Pow(10, gainDb / 40);
        double w0 = 2 * MathSupplement.DoublePi * centerFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double AL = sn * Math.Sinh(MathSupplement.DoubleLn2 / 2 * bandWidth * w0 / sn);
        if (double.IsNaN(AL)) {
            throw new("No solution available for these parameters.\n");
        }
        double b0 = 1 + AL * A;
        double b1 = -2 * cs;
        double b2 = 1 - AL * A;
        double a0 = 1 + AL / A;
        double a1 = -2 * cs;
        double a2 = 1 - AL / A;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(double sampleRate,
        double centerFrequency,
        double gainDb,
        double bandWidth) {
        SetupN(centerFrequency / sampleRate, gainDb, bandWidth);
    }
}

public class AllPass : RBJBase {
    private const double OneSqrtTwo = 0.707106781;

    public void SetupN(
        double phaseFrequency,
        double q = OneSqrtTwo) {
        double w0 = 2 * MathSupplement.DoublePi * phaseFrequency;
        double cs = Math.Cos(w0);
        double sn = Math.Sin(w0);
        double AL = sn / (2 * q);
        double b0 = 1 - AL;
        double b1 = -2 * cs;
        double b2 = 1 + AL;
        double a0 = 1 + AL;
        double a1 = -2 * cs;
        double a2 = 1 - AL;
        SetCoefficients(a0, a1, a2, b0, b1, b2);
    }

    public void Setup(double sampleRate,
        double phaseFrequency,
        double q = OneSqrtTwo) {
        SetupN(phaseFrequency / sampleRate, q);
    }
}