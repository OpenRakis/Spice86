namespace Spice86.Core.Emulator.Devices.Sound;

using System;

/// <summary>
/// Low-Frequency Oscillator (LFO) for TAL-Chorus modulation.
/// Provides various waveforms with linear interpolation for smooth modulation.
/// </summary>
/// <remarks>
/// Ported from DOSBox Staging: /src/libs/tal-chorus/Lfo.h and Lfo.cpp
/// 
/// Implementation by Remy Muller (2003-08-22)
/// Part of TAL-NoiseMaker by Patrick Kunz
/// Copyright (c) 2005-2010 Patrick Kunz, TAL - Togu Audio Line, Inc.
/// Licensed under GNU General Public License v2.0
/// http://kunz.corrupt.ch
/// 
/// Uses table-based synthesis with 256-entry lookup tables for each waveform.
/// Linear interpolation between table entries provides smooth output.
/// Phase ranges from 0.0 to 255.0 with fractional part for interpolation.
/// </remarks>
public sealed class Lfo {
    // Waveform lookup tables (257 entries: 0-256, where table[0] = table[256] for interpolation)
    private readonly float[] _tableSin = new float[257];
    private readonly float[] _tableTri = new float[257];
    private readonly float[] _tableSaw = new float[257];
    private readonly float[] _tableRec = new float[257];
    private readonly float[] _tableExp = new float[257];

    private readonly OscNoise _noiseOsc = new();
    private readonly Random _random = new();

    private float _phase;
    private float _inc;
    private float _sampleRate;
    private float _randomValue;
    private bool _freqWrap;

    private int _i;
    private float _frac;

    /// <summary>
    /// Gets the current LFO output value (raw, unsmoothed).
    /// </summary>
    public float Result { get; private set; }

    /// <summary>
    /// Gets the smoothed LFO output value.
    /// </summary>
    public float ResultSmooth { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Lfo"/> class.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    public Lfo(float sampleRate) {
        _phase = 0.0f;
        _inc = 0.0f;
        _sampleRate = sampleRate > 0.0f ? sampleRate : 44100.0f;
        _randomValue = 0.0f;
        ResultSmooth = 0.0f;

        // Initialize all waveform tables
        InitializeWaveforms();
        
        // Set default rate of 1Hz
        SetRate(1.0f);
    }

    /// <summary>
    /// Increments the phase and outputs the new LFO value.
    /// </summary>
    /// <param name="waveform">Waveform index (0=sine, 1=triangle, 2=saw, 3=rectangle, 4=exponential, other=noise).</param>
    /// <returns>The new LFO value between [-1.0, +1.0].</returns>
    public float Tick(int waveform) {
        _freqWrap = false;

        // Wrap phase at 256.0
        if (_phase > 255.0f) {
            _phase -= 255.0f;
            _freqWrap = true;
        }

        // Get integer and fractional parts for interpolation
        _i = (int)MathF.Floor(_phase);
        _frac = _phase - _i;

        // Increment phase for next tick
        _phase += _inc;

        // Select waveform and interpolate between table entries
        if (waveform == 0) {
            // Sine wave
            Result = _tableSin[_i] * (1.0f - _frac) + _tableSin[_i + 1] * _frac;
        } else if (waveform == 1) {
            // Triangle wave
            Result = _tableTri[_i] * (1.0f - _frac) + _tableTri[_i + 1] * _frac;
        } else if (waveform == 2) {
            // Sawtooth wave
            Result = _tableSaw[_i] * (1.0f - _frac) + _tableSaw[_i + 1] * _frac;
        } else if (waveform == 3) {
            // Rectangle wave
            Result = _tableRec[_i] * (1.0f - _frac) + _tableRec[_i + 1] * _frac;
        } else if (waveform == 4) {
            // Exponential (sample-and-hold random on wrap)
            if (_freqWrap) {
                _randomValue = (float)_random.NextDouble() * 2.0f - 1.0f;
            }
            Result = _randomValue;
        } else {
            // Noise oscillator
            Result = _noiseOsc.GetNextSample();
        }

        // Apply smoothing: single-pole lowpass filter
        // resultSmooth = (resultSmooth * 19 + result) * 0.05
        ResultSmooth = (ResultSmooth * 19.0f + Result) * 0.05f;
        return ResultSmooth;
    }

    /// <summary>
    /// Resets the LFO phase to a specific position.
    /// </summary>
    /// <param name="phase">Phase position (0.0-1.0, where 0.0 = start of cycle, 1.0 = end).</param>
    public void ResetPhase(float phase) {
        _phase = phase * 255.0f;
        _randomValue = (float)_random.NextDouble() * 2.0f - 1.0f;
    }

    /// <summary>
    /// Changes the current LFO rate.
    /// </summary>
    /// <param name="rate">New rate in Hz.</param>
    public void SetRate(float rate) {
        // Convert rate in Hz to phase increment per sample
        // inc = 256 * rate / sampleRate
        _inc = 256.0f * rate / _sampleRate;
    }

    /// <summary>
    /// Changes the current sample rate.
    /// </summary>
    /// <param name="sampleRate">New sample rate in Hz.</param>
    public void SetSampleRate(float sampleRate) {
        _sampleRate = sampleRate > 0.0f ? sampleRate : 44100.0f;
    }

    /// <summary>
    /// Initializes all waveform lookup tables.
    /// Called during construction and when waveform settings change.
    /// </summary>
    private void InitializeWaveforms() {
        // Waveform 0: Sine wave
        float pi = MathF.PI;
        for (int i = 0; i <= 256; i++) {
            _tableSin[i] = MathF.Sin(2.0f * pi * (i / 256.0f));
        }

        // Waveform 1: Triangle wave
        for (int i = 0; i < 64; i++) {
            _tableTri[i]       =         i / 64.0f;
            _tableTri[i + 64]  =   (64 - i) / 64.0f;
            _tableTri[i + 128] =       - i / 64.0f;
            _tableTri[i + 192] = - (64 - i) / 64.0f;
        }
        _tableTri[256] = 0.0f;

        // Waveform 2: Sawtooth wave
        for (int i = 0; i < 256; i++) {
            _tableSaw[i] = 2.0f * (i / 255.0f) - 1.0f;
        }
        _tableSaw[256] = -1.0f;

        // Waveform 3: Rectangle wave (square wave)
        for (int i = 0; i < 128; i++) {
            _tableRec[i]       =  1.0f;
            _tableRec[i + 128] = -1.0f;
        }
        _tableRec[256] = 1.0f;

        // Waveform 4: Exponential (symmetric, similar to triangle)
        float e = MathF.E;
        for (int i = 0; i < 128; i++) {
            _tableExp[i]       = 2.0f * ((MathF.Exp(i / 128.0f) - 1.0f) / (e - 1.0f)) - 1.0f;
            _tableExp[i + 128] = 2.0f * ((MathF.Exp((128 - i) / 128.0f) - 1.0f) / (e - 1.0f)) - 1.0f;
        }
        _tableExp[256] = -1.0f;
    }
}
