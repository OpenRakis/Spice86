// Port of Martin Eastwood's MVerb reverb algorithm
// Original source: https://github.com/dosbox-staging/dosbox-staging/blob/main/src/libs/mverb/MVerb.h
// Original license: GNU General Public License v3.0
//
// This code is distributed under the terms of the GNU General Public License
// MVerb is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// at your option) any later version.
//
// MVerb is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

namespace Spice86.Core.Emulator.Devices.Sound;

using System;

/// <summary>
/// Professional algorithmic reverb using Feedback Delay Network (FDN) architecture.
/// </summary>
public sealed class MVerb {
    private const int MaxLength = 96000; // Maximum buffer length for 96kHz sample rate

    // FDN components
    private readonly Allpass[] _allpass = new Allpass[4];
    private readonly StaticAllpassFourTap[] _allpassFourTap = new StaticAllpassFourTap[4];
    private readonly StateVariable[] _bandwidthFilter = new StateVariable[2];
    private readonly StateVariable[] _damping = new StateVariable[2];
    private readonly StaticDelayLine _predelay;
    private readonly StaticDelayLineFourTap[] _staticDelayLine = new StaticDelayLineFourTap[4];
    private readonly StaticDelayLineEightTap[] _earlyReflectionsDelayLine = new StaticDelayLineEightTap[2];

    // Parameters
    private float _sampleRate;
    private float _maxFreq;
    private float _dampingFreq;
    private float _density1;
    private float _density2;
    private float _bandwidthFreq;
    private float _preDelayTime;
    private float _decay;
    private float _gain;
    private float _mix;
    private float _earlyMix;
    private float _size;

    // Smoothing variables
    private float _mixSmooth;
    private float _earlyLateSmooth;
    private float _bandwidthSmooth;
    private float _dampingSmooth;
    private float _predelaySmooth;
    private float _sizeSmooth;
    private float _densitySmooth;
    private float _decaySmooth;

    // Tank state
    private float _previousLeftTank;
    private float _previousRightTank;

    // Control rate limiting
    private int _controlRate;
    private int _controlRateCounter;

    /// <summary>
    /// Parameter indices for SetParameter/GetParameter.
    /// </summary>
    public enum Parameter {
        DampingFreq = 0,
        Density,
        BandwidthFreq,
        Decay,
        Predelay,
        Size,
        Gain,
        Mix,
        EarlyMix,
        NumParams
    }

    /// <summary>
    /// Initializes a new instance of the MVerb reverb processor.
    /// </summary>
    public MVerb() {
        // Initialize components
        for (int i = 0; i < 4; i++) {
            _allpass[i] = new Allpass();
            _allpassFourTap[i] = new StaticAllpassFourTap();
            _staticDelayLine[i] = new StaticDelayLineFourTap();
        }

        for (int i = 0; i < 2; i++) {
            _bandwidthFilter[i] = new StateVariable();
            _damping[i] = new StateVariable();
            _earlyReflectionsDelayLine[i] = new StaticDelayLineEightTap();
        }

        _predelay = new StaticDelayLine();

        // Initialize parameters with defaults
        _dampingFreq = 0.9f;
        _bandwidthFreq = 0.9f;
        _sampleRate = 44100.0f;
        _maxFreq = 18400.0f;
        _decay = 0.5f;
        _gain = 1.0f;
        _mix = 1.0f;
        _size = 1.0f;
        _earlyMix = 1.0f;
        _previousLeftTank = 0.0f;
        _previousRightTank = 0.0f;
        _preDelayTime = 100.0f * (_sampleRate / 1000.0f);
        _mixSmooth = _earlyLateSmooth = _bandwidthSmooth = _dampingSmooth =
                     _predelaySmooth = _sizeSmooth = _decaySmooth = _densitySmooth = 0.0f;
        _controlRate = (int)(_sampleRate / 1000);
        _controlRateCounter = 0;

        Reset();
    }

    /// <summary>
    /// Processes stereo audio through the reverb.
    /// </summary>
    /// <param name="leftInput">Left channel input samples</param>
    /// <param name="rightInput">Right channel input samples</param>
    /// <param name="leftOutput">Left channel output samples</param>
    /// <param name="rightOutput">Right channel output samples</param>
    /// <param name="sampleFrames">Number of sample frames to process</param>
    public void Process(float[] leftInput, float[] rightInput,
                       float[] leftOutput, float[] rightOutput, int sampleFrames) {
        float oneOverSampleFrames = 1.0f / sampleFrames;
        float mixDelta = (_mix - _mixSmooth) * oneOverSampleFrames;
        float earlyLateDelta = (_earlyMix - _earlyLateSmooth) * oneOverSampleFrames;

        float bandwidthDelta = (((_bandwidthFreq * _maxFreq) + 100.0f) - _bandwidthSmooth) * oneOverSampleFrames;
        float dampingDelta = (((_dampingFreq * _maxFreq) + 100.0f) - _dampingSmooth) * oneOverSampleFrames;
        float predelayDelta = ((_preDelayTime * 200 * (_sampleRate / 1000)) - _predelaySmooth) * oneOverSampleFrames;
        float sizeDelta = (_size - _sizeSmooth) * oneOverSampleFrames;
        float decayDelta = (((0.7995f * _decay) + 0.005f) - _decaySmooth) * oneOverSampleFrames;
        float densityDelta = (((0.7995f * _density1) + 0.005f) - _densitySmooth) * oneOverSampleFrames;

        for (int i = 0; i < sampleFrames; ++i) {
            float left = leftInput[i];
            float right = rightInput[i];

            // Smooth parameter changes
            _mixSmooth += mixDelta;
            _earlyLateSmooth += earlyLateDelta;
            _bandwidthSmooth += bandwidthDelta;
            _dampingSmooth += dampingDelta;
            _predelaySmooth += predelayDelta;
            _sizeSmooth += sizeDelta;
            _decaySmooth += decayDelta;
            _densitySmooth += densityDelta;

            // Update filter frequencies at control rate
            if (_controlRateCounter >= _controlRate) {
                _controlRateCounter = 0;
                _bandwidthFilter[0].Frequency(_bandwidthSmooth);
                _bandwidthFilter[1].Frequency(_bandwidthSmooth);
                _damping[0].Frequency(_dampingSmooth);
                _damping[1].Frequency(_dampingSmooth);
            }
            ++_controlRateCounter;

            _predelay.SetLength((int)_predelaySmooth);

            // Calculate density2 based on decay
            _density2 = _decaySmooth + 0.15f;
            if (_density2 > 0.5f)
                _density2 = 0.5f;
            if (_density2 < 0.25f)
                _density2 = 0.25f;

            _allpassFourTap[1].SetFeedback(_density2);
            _allpassFourTap[3].SetFeedback(_density2);
            _allpassFourTap[0].SetFeedback(_density1);
            _allpassFourTap[2].SetFeedback(_density1);

            // Bandwidth filtering
            float bandwidthLeft = _bandwidthFilter[0].Process(left);
            float bandwidthRight = _bandwidthFilter[1].Process(right);

            // Early reflections calculation
            float earlyReflectionsL = _earlyReflectionsDelayLine[0].Process(bandwidthLeft * 0.5f + bandwidthRight * 0.3f)
                                    + _earlyReflectionsDelayLine[0].GetIndex(2) * 0.6f
                                    + _earlyReflectionsDelayLine[0].GetIndex(3) * 0.4f
                                    + _earlyReflectionsDelayLine[0].GetIndex(4) * 0.3f
                                    + _earlyReflectionsDelayLine[0].GetIndex(5) * 0.3f
                                    + _earlyReflectionsDelayLine[0].GetIndex(6) * 0.1f
                                    + _earlyReflectionsDelayLine[0].GetIndex(7) * 0.1f
                                    + (bandwidthLeft * 0.4f + bandwidthRight * 0.2f) * 0.5f;

            float earlyReflectionsR = _earlyReflectionsDelayLine[1].Process(bandwidthLeft * 0.3f + bandwidthRight * 0.5f)
                                    + _earlyReflectionsDelayLine[1].GetIndex(2) * 0.6f
                                    + _earlyReflectionsDelayLine[1].GetIndex(3) * 0.4f
                                    + _earlyReflectionsDelayLine[1].GetIndex(4) * 0.3f
                                    + _earlyReflectionsDelayLine[1].GetIndex(5) * 0.3f
                                    + _earlyReflectionsDelayLine[1].GetIndex(6) * 0.1f
                                    + _earlyReflectionsDelayLine[1].GetIndex(7) * 0.1f
                                    + (bandwidthLeft * 0.2f + bandwidthRight * 0.4f) * 0.5f;

            // Predelay
            float predelayMonoInput = _predelay.Process((bandwidthRight + bandwidthLeft) * 0.5f);

            // Allpass diffusion
            float smearedInput = predelayMonoInput;
            for (int j = 0; j < 4; j++)
                smearedInput = _allpass[j].Process(smearedInput);

            // FDN feedback network - Left tank
            float leftTank = _allpassFourTap[0].Process(smearedInput + _previousRightTank);
            leftTank = _staticDelayLine[0].Process(leftTank);
            leftTank = _damping[0].Process(leftTank);
            leftTank = _allpassFourTap[1].Process(leftTank);
            leftTank = _staticDelayLine[1].Process(leftTank);

            // FDN feedback network - Right tank
            float rightTank = _allpassFourTap[2].Process(smearedInput + _previousLeftTank);
            rightTank = _staticDelayLine[2].Process(rightTank);
            rightTank = _damping[1].Process(rightTank);
            rightTank = _allpassFourTap[3].Process(rightTank);
            rightTank = _staticDelayLine[3].Process(rightTank);

            // Update tank state
            _previousLeftTank = leftTank * _decaySmooth;
            _previousRightTank = rightTank * _decaySmooth;

            // Output accumulation from delay taps
            float accumulatorL = (0.6f * _staticDelayLine[2].GetIndex(1))
                              + (0.6f * _staticDelayLine[2].GetIndex(2))
                              - (0.6f * _allpassFourTap[3].GetIndex(1))
                              + (0.6f * _staticDelayLine[3].GetIndex(1))
                              - (0.6f * _staticDelayLine[0].GetIndex(1))
                              - (0.6f * _allpassFourTap[1].GetIndex(1))
                              - (0.6f * _staticDelayLine[1].GetIndex(1));

            float accumulatorR = (0.6f * _staticDelayLine[0].GetIndex(2))
                              + (0.6f * _staticDelayLine[0].GetIndex(3))
                              - (0.6f * _allpassFourTap[1].GetIndex(2))
                              + (0.6f * _staticDelayLine[1].GetIndex(2))
                              - (0.6f * _staticDelayLine[2].GetIndex(3))
                              - (0.6f * _allpassFourTap[3].GetIndex(2))
                              - (0.6f * _staticDelayLine[3].GetIndex(2));

            // Mix early reflections with late reverberation
            accumulatorL = (accumulatorL * _earlyMix) + ((1 - _earlyMix) * earlyReflectionsL);
            accumulatorR = (accumulatorR * _earlyMix) + ((1 - _earlyMix) * earlyReflectionsR);

            // Apply mix and gain
            left = (left + _mixSmooth * (accumulatorL - left)) * _gain;
            right = (right + _mixSmooth * (accumulatorR - right)) * _gain;

            leftOutput[i] = left;
            rightOutput[i] = right;
        }
    }

    /// <summary>
    /// Resets all delay lines and filters to initial state.
    /// </summary>
    public void Reset() {
        _controlRateCounter = 0;

        // Setup bandwidth filters
        _bandwidthFilter[0].SetSampleRate(_sampleRate);
        _bandwidthFilter[1].SetSampleRate(_sampleRate);
        _bandwidthFilter[0].Reset();
        _bandwidthFilter[1].Reset();

        // Setup damping filters
        _damping[0].SetSampleRate(_sampleRate);
        _damping[1].SetSampleRate(_sampleRate);
        _damping[0].Reset();
        _damping[1].Reset();

        // Setup predelay
        _predelay.Clear();
        _predelay.SetLength((int)_preDelayTime);

        // Setup diffusion allpass filters
        _allpass[0].Clear();
        _allpass[1].Clear();
        _allpass[2].Clear();
        _allpass[3].Clear();
        _allpass[0].SetLength((int)(0.0048 * _sampleRate));
        _allpass[1].SetLength((int)(0.0036 * _sampleRate));
        _allpass[2].SetLength((int)(0.0127 * _sampleRate));
        _allpass[3].SetLength((int)(0.0093 * _sampleRate));
        _allpass[0].SetFeedback(0.75f);
        _allpass[1].SetFeedback(0.75f);
        _allpass[2].SetFeedback(0.625f);
        _allpass[3].SetFeedback(0.625f);

        // Setup FDN allpass filters
        _allpassFourTap[0].Clear();
        _allpassFourTap[1].Clear();
        _allpassFourTap[2].Clear();
        _allpassFourTap[3].Clear();
        _allpassFourTap[0].SetLength((int)(0.020 * _sampleRate * _size));
        _allpassFourTap[1].SetLength((int)(0.060 * _sampleRate * _size));
        _allpassFourTap[2].SetLength((int)(0.030 * _sampleRate * _size));
        _allpassFourTap[3].SetLength((int)(0.089 * _sampleRate * _size));
        _allpassFourTap[0].SetFeedback(_density1);
        _allpassFourTap[1].SetFeedback(_density2);
        _allpassFourTap[2].SetFeedback(_density1);
        _allpassFourTap[3].SetFeedback(_density2);
        _allpassFourTap[0].SetIndex(0, 0, 0, 0);
        _allpassFourTap[1].SetIndex(0, (int)(0.006 * _sampleRate * _size), (int)(0.041 * _sampleRate * _size), 0);
        _allpassFourTap[2].SetIndex(0, 0, 0, 0);
        _allpassFourTap[3].SetIndex(0, (int)(0.031 * _sampleRate * _size), (int)(0.011 * _sampleRate * _size), 0);

        // Setup delay lines
        _staticDelayLine[0].Clear();
        _staticDelayLine[1].Clear();
        _staticDelayLine[2].Clear();
        _staticDelayLine[3].Clear();
        _staticDelayLine[0].SetLength((int)(0.15 * _sampleRate * _size));
        _staticDelayLine[1].SetLength((int)(0.12 * _sampleRate * _size));
        _staticDelayLine[2].SetLength((int)(0.14 * _sampleRate * _size));
        _staticDelayLine[3].SetLength((int)(0.11 * _sampleRate * _size));
        _staticDelayLine[0].SetIndex(0, (int)(0.067 * _sampleRate * _size), (int)(0.011 * _sampleRate * _size), (int)(0.121 * _sampleRate * _size));
        _staticDelayLine[1].SetIndex(0, (int)(0.036 * _sampleRate * _size), (int)(0.089 * _sampleRate * _size), 0);
        _staticDelayLine[2].SetIndex(0, (int)(0.0089 * _sampleRate * _size), (int)(0.099 * _sampleRate * _size), 0);
        _staticDelayLine[3].SetIndex(0, (int)(0.067 * _sampleRate * _size), (int)(0.0041 * _sampleRate * _size), 0);

        // Setup early reflections
        _earlyReflectionsDelayLine[0].Clear();
        _earlyReflectionsDelayLine[1].Clear();
        _earlyReflectionsDelayLine[0].SetLength((int)(0.089 * _sampleRate));
        _earlyReflectionsDelayLine[0].SetIndex(0, (int)(0.0199 * _sampleRate), (int)(0.0219 * _sampleRate),
            (int)(0.0354 * _sampleRate), (int)(0.0389 * _sampleRate), (int)(0.0414 * _sampleRate),
            (int)(0.0692 * _sampleRate), 0);
        _earlyReflectionsDelayLine[1].SetLength((int)(0.069 * _sampleRate));
        _earlyReflectionsDelayLine[1].SetIndex(0, (int)(0.0099 * _sampleRate), (int)(0.011 * _sampleRate),
            (int)(0.0182 * _sampleRate), (int)(0.0189 * _sampleRate), (int)(0.0213 * _sampleRate),
            (int)(0.0431 * _sampleRate), 0);
    }

    /// <summary>
    /// Sets a reverb parameter.
    /// </summary>
    public void SetParameter(Parameter index, float value) {
        switch (index) {
            case Parameter.DampingFreq:
                _dampingFreq = 1.0f - value;
                break;
            case Parameter.Density:
                _density1 = value;
                break;
            case Parameter.BandwidthFreq:
                _bandwidthFreq = value;
                break;
            case Parameter.Predelay:
                _preDelayTime = value;
                break;
            case Parameter.Size:
                _size = (0.95f * value) + 0.05f;
                _allpassFourTap[0].Clear();
                _allpassFourTap[1].Clear();
                _allpassFourTap[2].Clear();
                _allpassFourTap[3].Clear();
                _allpassFourTap[0].SetLength((int)(0.020 * _sampleRate * _size));
                _allpassFourTap[1].SetLength((int)(0.060 * _sampleRate * _size));
                _allpassFourTap[2].SetLength((int)(0.030 * _sampleRate * _size));
                _allpassFourTap[3].SetLength((int)(0.089 * _sampleRate * _size));
                _allpassFourTap[1].SetIndex(0, (int)(0.006 * _sampleRate * _size), (int)(0.041 * _sampleRate * _size), 0);
                _allpassFourTap[3].SetIndex(0, (int)(0.031 * _sampleRate * _size), (int)(0.011 * _sampleRate * _size), 0);
                _staticDelayLine[0].Clear();
                _staticDelayLine[1].Clear();
                _staticDelayLine[2].Clear();
                _staticDelayLine[3].Clear();
                _staticDelayLine[0].SetLength((int)(0.15 * _sampleRate * _size));
                _staticDelayLine[1].SetLength((int)(0.12 * _sampleRate * _size));
                _staticDelayLine[2].SetLength((int)(0.14 * _sampleRate * _size));
                _staticDelayLine[3].SetLength((int)(0.11 * _sampleRate * _size));
                _staticDelayLine[0].SetIndex(0, (int)(0.067 * _sampleRate * _size), (int)(0.011 * _sampleRate * _size), (int)(0.121 * _sampleRate * _size));
                _staticDelayLine[1].SetIndex(0, (int)(0.036 * _sampleRate * _size), (int)(0.089 * _sampleRate * _size), 0);
                _staticDelayLine[2].SetIndex(0, (int)(0.0089 * _sampleRate * _size), (int)(0.099 * _sampleRate * _size), 0);
                _staticDelayLine[3].SetIndex(0, (int)(0.067 * _sampleRate * _size), (int)(0.0041 * _sampleRate * _size), 0);
                break;
            case Parameter.Decay:
                _decay = value;
                break;
            case Parameter.Gain:
                _gain = value;
                break;
            case Parameter.Mix:
                _mix = value;
                break;
            case Parameter.EarlyMix:
                _earlyMix = value;
                break;
        }
    }

    /// <summary>
    /// Gets a reverb parameter value.
    /// </summary>
    public float GetParameter(Parameter index) {
        switch (index) {
            case Parameter.DampingFreq:
                return _dampingFreq * 100.0f;
            case Parameter.Density:
                return _density1 * 100.0f;
            case Parameter.BandwidthFreq:
                return _bandwidthFreq * 100.0f;
            case Parameter.Predelay:
                return _preDelayTime * 100.0f;
            case Parameter.Size:
                return ((0.95f * _size) + 0.05f) * 100.0f;
            case Parameter.Decay:
                return _decay * 100.0f;
            case Parameter.Gain:
                return _gain * 100.0f;
            case Parameter.Mix:
                return _mix * 100.0f;
            case Parameter.EarlyMix:
                return _earlyMix * 100.0f;
            default:
                return 0.0f;
        }
    }

    /// <summary>
    /// Sets the sample rate and recalculates all internal parameters.
    /// </summary>
    public void SetSampleRate(float sampleRate) {
        _sampleRate = sampleRate;
        _controlRate = (int)(_sampleRate / 1000);
        _maxFreq = Math.Min(_sampleRate * 0.41723f, 18400.0f);
        Reset();
    }


    /// <summary>
    /// Allpass filter for diffusion.
    /// </summary>
    private sealed class Allpass {
        private readonly float[] _buffer = new float[MaxLength];
        private int _index;
        private int _length;
        private float _feedback;

        public Allpass() {
            SetLength(MaxLength - 1);
            Clear();
            _feedback = 0.5f;
        }

        public float Process(float input) {
            float bufout = _buffer[_index];
            float temp = input * -_feedback;
            float output = bufout + temp;
            _buffer[_index] = input + ((bufout + temp) * _feedback);
            if (++_index >= _length)
                _index = 0;
            return output;
        }

        public void SetLength(int length) {
            if (length >= MaxLength)
                length = MaxLength;
            if (length < 0)
                length = 0;
            _length = length;
        }

        public void SetFeedback(float feedback) {
            _feedback = feedback;
        }

        public void Clear() {
            Array.Clear(_buffer, 0, _buffer.Length);
            _index = 0;
        }
    }

    /// <summary>
    /// Allpass filter with four tap points for complex reflections.
    /// </summary>
    private sealed class StaticAllpassFourTap {
        private readonly float[] _buffer = new float[MaxLength];
        private int _index1;
        private int _index2;
        private int _index3;
        private int _index4;
        private int _length;
        private float _feedback;

        public StaticAllpassFourTap() {
            SetLength(MaxLength - 1);
            Clear();
            _feedback = 0.5f;
        }

        public float Process(float input) {
            float bufout = _buffer[_index1];
            float temp = input * -_feedback;
            float output = bufout + temp;
            _buffer[_index1] = input + ((bufout + temp) * _feedback);

            if (++_index1 >= _length)
                _index1 = 0;
            if (++_index2 >= _length)
                _index2 = 0;
            if (++_index3 >= _length)
                _index3 = 0;
            if (++_index4 >= _length)
                _index4 = 0;

            return output;
        }

        public void SetIndex(int index1, int index2, int index3, int index4) {
            _index1 = index1;
            _index2 = index2;
            _index3 = index3;
            _index4 = index4;
        }

        public float GetIndex(int index) {
            return index switch {
                0 => _buffer[_index1],
                1 => _buffer[_index2],
                2 => _buffer[_index3],
                3 => _buffer[_index4],
                _ => _buffer[_index1],
            };
        }

        public void SetLength(int length) {
            if (length >= MaxLength)
                length = MaxLength;
            if (length < 0)
                length = 0;
            _length = length;
        }

        public void Clear() {
            Array.Clear(_buffer, 0, _buffer.Length);
            _index1 = _index2 = _index3 = _index4 = 0;
        }

        public void SetFeedback(float feedback) {
            _feedback = feedback;
        }
    }

    /// <summary>
    /// Simple delay line without feedback.
    /// </summary>
    private sealed class StaticDelayLine {
        private readonly float[] _buffer = new float[MaxLength];
        private int _index;
        private int _length;

        public StaticDelayLine() {
            SetLength(MaxLength - 1);
            Clear();
        }

        public float Process(float input) {
            float output = _buffer[_index];
            _buffer[_index++] = input;
            if (_index >= _length)
                _index = 0;
            return output;
        }

        public void SetLength(int length) {
            if (length >= MaxLength)
                length = MaxLength;
            if (length < 0)
                length = 0;
            _length = length;
        }

        public void Clear() {
            Array.Clear(_buffer, 0, _buffer.Length);
            _index = 0;
        }
    }

    /// <summary>
    /// Delay line with four tap points for complex delay patterns.
    /// </summary>
    private sealed class StaticDelayLineFourTap {
        private readonly float[] _buffer = new float[MaxLength];
        private int _index1;
        private int _index2;
        private int _index3;
        private int _index4;
        private int _length;

        public StaticDelayLineFourTap() {
            SetLength(MaxLength - 1);
            Clear();
        }

        public float Process(float input) {
            float output = _buffer[_index1];
            _buffer[_index1++] = input;
            if (_index1 >= _length)
                _index1 = 0;
            if (++_index2 >= _length)
                _index2 = 0;
            if (++_index3 >= _length)
                _index3 = 0;
            if (++_index4 >= _length)
                _index4 = 0;
            return output;
        }

        public void SetIndex(int index1, int index2, int index3, int index4) {
            _index1 = index1;
            _index2 = index2;
            _index3 = index3;
            _index4 = index4;
        }

        public float GetIndex(int index) {
            return index switch {
                0 => _buffer[_index1],
                1 => _buffer[_index2],
                2 => _buffer[_index3],
                3 => _buffer[_index4],
                _ => _buffer[_index1],
            };
        }

        public void SetLength(int length) {
            if (length >= MaxLength)
                length = MaxLength;
            if (length < 0)
                length = 0;
            _length = length;
        }

        public void Clear() {
            Array.Clear(_buffer, 0, _buffer.Length);
            _index1 = _index2 = _index3 = _index4 = 0;
        }
    }

    /// <summary>
    /// Delay line with eight tap points for early reflections.
    /// </summary>
    private sealed class StaticDelayLineEightTap {
        private readonly float[] _buffer = new float[MaxLength];
        private int _index1;
        private int _index2;
        private int _index3;
        private int _index4;
        private int _index5;
        private int _index6;
        private int _index7;
        private int _index8;
        private int _length;

        public StaticDelayLineEightTap() {
            SetLength(MaxLength - 1);
            Clear();
        }

        public float Process(float input) {
            float output = _buffer[_index1];
            _buffer[_index1++] = input;
            if (_index1 >= _length)
                _index1 = 0;
            if (++_index2 >= _length)
                _index2 = 0;
            if (++_index3 >= _length)
                _index3 = 0;
            if (++_index4 >= _length)
                _index4 = 0;
            if (++_index5 >= _length)
                _index5 = 0;
            if (++_index6 >= _length)
                _index6 = 0;
            if (++_index7 >= _length)
                _index7 = 0;
            if (++_index8 >= _length)
                _index8 = 0;
            return output;
        }

        public void SetIndex(int index1, int index2, int index3, int index4,
                            int index5, int index6, int index7, int index8) {
            _index1 = index1;
            _index2 = index2;
            _index3 = index3;
            _index4 = index4;
            _index5 = index5;
            _index6 = index6;
            _index7 = index7;
            _index8 = index8;
        }

        public float GetIndex(int index) {
            return index switch {
                0 => _buffer[_index1],
                1 => _buffer[_index2],
                2 => _buffer[_index3],
                3 => _buffer[_index4],
                4 => _buffer[_index5],
                5 => _buffer[_index6],
                6 => _buffer[_index7],
                7 => _buffer[_index8],
                _ => _buffer[_index1],
            };
        }

        public void SetLength(int length) {
            if (length >= MaxLength)
                length = MaxLength;
            if (length < 0)
                length = 0;
            _length = length;
        }

        public void Clear() {
            Array.Clear(_buffer, 0, _buffer.Length);
            _index1 = _index2 = _index3 = _index4 = _index5 = _index6 = _index7 = _index8 = 0;
        }
    }

    /// <summary>
    /// State variable filter for frequency-dependent damping.
    /// </summary>
    private sealed class StateVariable {
        private const int OverSampleCount = 4;

        private enum FilterType {
            Lowpass,
            Highpass,
            Bandpass,
            Notch,
            FilterTypeCount
        }

        private float _sampleRate;
        private float _frequency;
        private float _q;
        private float _f;

        private float _low;
        private float _high;
        private float _band;
        private float _notch;

        private FilterType _currentType;

        public StateVariable() {
            SetSampleRate(44100.0f);
            Frequency(1000.0f);
            Resonance(0);
            Type(FilterType.Lowpass);
            Reset();
        }

        public float Process(float input) {
            for (int i = 0; i < OverSampleCount; i++) {
                _low += _f * _band + 1e-25f;
                _high = input - _low - _q * _band;
                _band += _f * _high;
                _notch = _low + _high;
            }

            return _currentType switch {
                FilterType.Lowpass => _low,
                FilterType.Highpass => _high,
                FilterType.Bandpass => _band,
                FilterType.Notch => _notch,
                _ => _low,
            };
        }

        public void Reset() {
            _low = _high = _band = _notch = 0;
        }

        public void SetSampleRate(float sampleRate) {
            _sampleRate = sampleRate * OverSampleCount;
            UpdateCoefficient();
        }

        public void Frequency(float frequency) {
            _frequency = frequency;
            UpdateCoefficient();
        }

        public void Resonance(float resonance) {
            _q = 2 - 2 * resonance;
        }

        private void Type(FilterType type) {
            _currentType = type;
        }

        private void UpdateCoefficient() {
            _f = (float)(2.0 * Math.Sin(Math.PI * _frequency / _sampleRate));
        }
    }
}
