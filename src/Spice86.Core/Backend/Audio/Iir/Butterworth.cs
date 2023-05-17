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
 *         User facing class which contains all the methods the user uses
 *         to create Butterworth filters. This done in this way:
 *         Butterworth butterworth = new Butterworth();
 *         Then call one of the methods below to create
 *         low-,high-,band-, or stopband filters. For example:
 *         butterworth.bandPass(2,250,50,5);
 */
public class Butterworth : Cascade {
    class AnalogLowPass : LayoutBase {
        private readonly int _nPoles;
        public AnalogLowPass(int nPoles) : base(nPoles) {
            _nPoles = nPoles;
            SetNormal(0, 1);
        }

        public void Design() {
            Reset();
            double n2 = 2 * _nPoles;
            int pairs = _nPoles / 2;
            for (int i = 0; i < pairs; ++i) {
                Complex c = ComplexUtils.PolarToComplex(1F, Math.PI / 2.0
                        + (2 * i + 1) * Math.PI / n2);
                AddPoleZeroConjugatePairs(c, Complex.Infinity);
            }

            if ((_nPoles & 1) == 1) {
                Add(new Complex(-1, 0), Complex.Infinity);
            }
        }
    }

    private void SetupLowPass(int order, double sampleRate,
            double cutoffFrequency, int directFormType) {

        var analogProto = new AnalogLowPass(order);
        analogProto.Design();

        var digitalProto = new LayoutBase(order);

        new LowPassTransform(cutoffFrequency / sampleRate, digitalProto,
                analogProto);

        SetLayout(digitalProto, directFormType);
    }

    /**
	 * Butterworth Lowpass filter with default topology
	 *
	 * @param order
	 *            The order of the filter
	 * @param sampleRate
	 *            The sampling rate of the system
	 * @param cutoffFrequency
	 *            the cutoff frequency
	 */
    public void LowPass(int order, double sampleRate, double cutoffFrequency) {
        SetupLowPass(order, sampleRate, cutoffFrequency,
                DirectFormAbstract.DirectFormII);
    }

    /**
	 * Butterworth Lowpass filter with custom topology
	 *
	 * @param order
	 *            The order of the filter
	 * @param sampleRate
	 *            The sampling rate of the system
	 * @param cutoffFrequency
	 *            The cutoff frequency
	 * @param directFormType
	 *            The filter topology. This is either
	 *            DirectFormAbstract.DIRECT_FORM_I or DIRECT_FORM_II
	 */
    public void LowPass(int order, double sampleRate, double cutoffFrequency,
            int directFormType) {
        SetupLowPass(order, sampleRate, cutoffFrequency, directFormType);
    }

    private void SetupHighPass(int order, double sampleRate,
            double cutoffFrequency, int directFormType) {

        var analogProto = new AnalogLowPass(order);
        analogProto.Design();

        var digitalProto = new LayoutBase(order);

        new HighPassTransform(cutoffFrequency / sampleRate, digitalProto,
                analogProto);

        SetLayout(digitalProto, directFormType);
    }

    /**
	 * Highpass filter with custom topology
	 *
	 * @param order
	 *            Filter order (ideally only even orders)
	 * @param sampleRate
	 *            Sampling rate of the system
	 * @param cutoffFrequency
	 *            Cutoff of the system
	 * @param directFormType
	 *            The filter topology. See DirectFormAbstract.
	 */
    public void HighPass(int order, double sampleRate, double cutoffFrequency,
            int directFormType) {
        SetupHighPass(order, sampleRate, cutoffFrequency, directFormType);
    }

    /**
	 * Highpass filter with default filter topology
	 *
	 * @param order
	 *            Filter order (ideally only even orders)
	 * @param sampleRate
	 *            Sampling rate of the system
	 * @param cutoffFrequency
	 *            Cutoff of the system
	 */
    public void HighPass(int order, double sampleRate, double cutoffFrequency) {
        SetupHighPass(order, sampleRate, cutoffFrequency,
                DirectFormAbstract.DirectFormII);
    }

    private void SetupBandStop(int order, double sampleRate,
            double centerFrequency, double widthFrequency, int directFormType) {

        var analogProto = new AnalogLowPass(order);
        analogProto.Design();

        var m_digitalProto = new LayoutBase(order * 2);

        new BandStopTransform(centerFrequency / sampleRate, widthFrequency
                / sampleRate, m_digitalProto, analogProto);

        SetLayout(m_digitalProto, directFormType);
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
	 */
    public void BandStop(int order, double sampleRate, double centerFrequency,
            double widthFrequency) {
        SetupBandStop(order, sampleRate, centerFrequency, widthFrequency,
                DirectFormAbstract.DirectFormII);
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
	 * @param directFormType
	 *            The filter topology
	 */
    public void BandStop(int order, double sampleRate, double centerFrequency,
            double widthFrequency, int directFormType) {
        SetupBandStop(order, sampleRate, centerFrequency, widthFrequency,
                directFormType);
    }

    private void SetupBandPass(int order, double sampleRate,
            double centerFrequency, double widthFrequency, int directFormType) {
        var m_analogProto = new AnalogLowPass(order);
        m_analogProto.Design();

        var m_digitalProto = new LayoutBase(order * 2);

        new BandPassTransform(centerFrequency / sampleRate, widthFrequency
                / sampleRate, m_digitalProto, m_analogProto);

        SetLayout(m_digitalProto, directFormType);
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
	 */
    public void BandPass(int order, double sampleRate, double centerFrequency,
            double widthFrequency) {
        SetupBandPass(order, sampleRate, centerFrequency, widthFrequency,
                DirectFormAbstract.DirectFormII);
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
	 * @param directFormType
	 *            The filter topology (see DirectFormAbstract)
	 */
    public void BandPass(int order, double sampleRate, double centerFrequency,
            double widthFrequency, int directFormType) {
        SetupBandPass(order, sampleRate, centerFrequency, widthFrequency,
                directFormType);
    }
}
