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
 * User facing class which contains all the methods the user uses to create
 * ChebyshevI filters. This done in this way: ChebyshevI chebyshevI = new
 * ChebyshevI(); Then call one of the methods below to create low-,high-,band-,
 * or stopband filters. For example: chebyshevI.bandPass(2,250,50,5,0.5);
 */
public class ChebyshevI : Cascade {
    class AnalogLowPass : LayoutBase {
        private readonly int _nPoles;

        public AnalogLowPass(int nPoles) : base(nPoles) {
            _nPoles = nPoles;
        }

        public void Design(double rippleDb) {
            Reset();

            double eps = Math.Sqrt(1.0 / Math.Exp(-rippleDb * 0.1 * MathSupplement.DoubleLn10) - 1);
            double v0 = MathSupplement.Asinh(1 / eps) / _nPoles;
            double sinh_v0 = -Math.Sinh(v0);
            double cosh_v0 = Math.Cosh(v0);

            double n2 = 2 * _nPoles;
            int pairs = _nPoles / 2;
            for (int i = 0; i < pairs; ++i) {
                int k = 2 * i + 1 - _nPoles;
                double a = sinh_v0 * Math.Cos(k * Math.PI / n2);
                double b = cosh_v0 * Math.Sin(k * Math.PI / n2);

                AddPoleZeroConjugatePairs(new Complex(a, b), new Complex(
                        double.PositiveInfinity, 0));
            }

            if ((_nPoles & 1) == 1) {
                Add(new Complex(sinh_v0, 0), new Complex(
                        double.PositiveInfinity, 0));
                SetNormal(0, 1);
            } else {
                SetNormal(0, Math.Pow(10, -rippleDb / 20.0));
            }
        }
    }

    private void SetupLowPass(int order, double sampleRate,
            double cutoffFrequency, double rippleDb, int directFormType) {

        var m_analogProto = new AnalogLowPass(order);
        m_analogProto.Design(rippleDb);

        var m_digitalProto = new LayoutBase(order);

        new LowPassTransform(cutoffFrequency / sampleRate, m_digitalProto,
                m_analogProto);

        SetLayout(m_digitalProto, directFormType);
    }

    /**
	 * ChebyshevI Lowpass filter with default toplogy
	 *
	 * @param order
	 *            The order of the filter
	 * @param sampleRate
	 *            The sampling rate of the system
	 * @param cutoffFrequency
	 *            the cutoff frequency
	 * @param rippleDb
	 *            passband ripple in decibel sensible value: 1dB
	 */
    public void LowPass(int order, double sampleRate, double cutoffFrequency,
            double rippleDb) {
        SetupLowPass(order, sampleRate, cutoffFrequency, rippleDb,
                DirectFormAbstract.DirectFormII);
    }

    /**
	 * ChebyshevI Lowpass filter with custom topology
	 *
	 * @param order
	 *            The order of the filter
	 * @param sampleRate
	 *            The sampling rate of the system
	 * @param cutoffFrequency
	 *            The cutoff frequency
	 * @param rippleDb
	 *            passband ripple in decibel sensible value: 1dB
	 * @param directFormType
	 *            The filter topology. This is either
	 *            DirectFormAbstract.DIRECT_FORM_I or DIRECT_FORM_II
	 */
    public void LowPass(int order, double sampleRate, double cutoffFrequency,
            double rippleDb, int directFormType) {
        SetupLowPass(order, sampleRate, cutoffFrequency, rippleDb,
                directFormType);
    }

    private void SetupHighPass(int order, double sampleRate,
            double cutoffFrequency, double rippleDb, int directFormType) {
        var analogProto = new AnalogLowPass(order);
        analogProto.Design(rippleDb);

        var digitalProto = new LayoutBase(order);

        new HighPassTransform(cutoffFrequency / sampleRate, digitalProto,
                analogProto);

        SetLayout(digitalProto, directFormType);
    }

    /**
	 * ChebyshevI Highpass filter with default topology
	 *
	 * @param order
	 *            The order of the filter
	 * @param sampleRate
	 *            The sampling rate of the system
	 * @param cutoffFrequency
	 *            the cutoff frequency
	 * @param rippleDb
	 *            passband ripple in decibel sensible value: 1dB
	 */
    public void HighPass(int order, double sampleRate, double cutoffFrequency,
            double rippleDb) {
        SetupHighPass(order, sampleRate, cutoffFrequency, rippleDb,
                DirectFormAbstract.DirectFormII);
    }

    /**
	 * ChebyshevI Lowpass filter and custom filter topology
	 *
	 * @param order
	 *            The order of the filter
	 * @param sampleRate
	 *            The sampling rate of the system
	 * @param cutoffFrequency
	 *            The cutoff frequency
	 * @param rippleDb
	 *            passband ripple in decibel sensible value: 1dB
	 * @param directFormType
	 *            The filter topology. This is either
	 *            DirectFormAbstract.DIRECT_FORM_I or DIRECT_FORM_II
	 */
    public void HighPass(int order, double sampleRate, double cutoffFrequency,
            double rippleDb, int directFormType) {
        SetupHighPass(order, sampleRate, cutoffFrequency, rippleDb,
                directFormType);
    }

    private void SetupBandStop(int order, double sampleRate,
            double centerFrequency, double widthFrequency, double rippleDb,
            int directFormType) {

        var analogProto = new AnalogLowPass(order);
        analogProto.Design(rippleDb);

        var digitalProto = new LayoutBase(order * 2);

        new BandStopTransform(centerFrequency / sampleRate, widthFrequency
                / sampleRate, digitalProto, analogProto);

        SetLayout(digitalProto, directFormType);
    }

    /**
	 * Bandstop filter with default topology
	 * 
	 * @param order
	 *            Filter order (actual order is twice)
	 * @param sampleRate
	 *            Samping rate of the system
	 * @param centerFrequency
	 *            Center frequency
	 * @param widthFrequency
	 *            Width of the notch
	 * @param rippleDb
	 *            passband ripple in decibel sensible value: 1dB
	 */
    public void BandStop(int order, double sampleRate, double centerFrequency,
            double widthFrequency, double rippleDb) {
        SetupBandStop(order, sampleRate, centerFrequency, widthFrequency,
                rippleDb, DirectFormAbstract.DirectFormII);
    }

    /**
	 * Bandstop filter with custom topology
	 * 
	 * @param order
	 *            Filter order (actual order is twice)
	 * @param sampleRate
	 *            Samping rate of the system
	 * @param centerFrequency
	 *            Center frequency
	 * @param widthFrequency
	 *            Width of the notch
	 * @param rippleDb
	 *            passband ripple in decibel sensible value: 1dB
	 * @param directFormType
	 *            The filter topology
	 */
    public void BandStop(int order, double sampleRate, double centerFrequency,
            double widthFrequency, double rippleDb, int directFormType) {
        SetupBandStop(order, sampleRate, centerFrequency, widthFrequency,
                rippleDb, directFormType);
    }

    private void SetupBandPass(int order, double sampleRate,
            double centerFrequency, double widthFrequency, double rippleDb,
            int directFormType) {

        var analogProto = new AnalogLowPass(order);
        analogProto.Design(rippleDb);

        var digitalProto = new LayoutBase(order * 2);

        new BandPassTransform(centerFrequency / sampleRate, widthFrequency
                / sampleRate, digitalProto, analogProto);

        SetLayout(digitalProto, directFormType);

    }

    /**
	 * Bandpass filter with default topology
	 * 
	 * @param order
	 *            Filter order
	 * @param sampleRate
	 *            Sampling rate
	 * @param centerFrequency
	 *            Center frequency
	 * @param widthFrequency
	 *            Width of the notch
	 * @param rippleDb
	 *            passband ripple in decibel sensible value: 1dB
	 */
    public void BandPass(int order, double sampleRate, double centerFrequency,
            double widthFrequency, double rippleDb) {
        SetupBandPass(order, sampleRate, centerFrequency, widthFrequency,
                rippleDb, DirectFormAbstract.DirectFormII);
    }

    /**
	 * Bandpass filter with custom topology
	 * 
	 * @param order
	 *            Filter order
	 * @param sampleRate
	 *            Sampling rate
	 * @param centerFrequency
	 *            Center frequency
	 * @param widthFrequency
	 *            Width of the notch
	 * @param rippleDb
	 *            passband ripple in decibel sensible value: 1dB
	 * @param directFormType
	 *            The filter topology (see DirectFormAbstract)
	 */
    public void BandPass(int order, double sampleRate, double centerFrequency,
            double widthFrequency, double rippleDb, int directFormType) {
        SetupBandPass(order, sampleRate, centerFrequency, widthFrequency,
                rippleDb, directFormType);
    }

}
