namespace Spice86.Core.Emulator.Devices.Sound;

using System;

/// <summary>
/// Single chorus line with LFO-modulated delay for TAL-Chorus.
/// Implements a modulated delay line that creates the chorus effect by varying
/// the delay time, which produces subtle pitch shifts and thickening.
/// </summary>
/// <remarks>
/// Ported from DOSBox Staging: /src/libs/tal-chorus/Chorus.h
/// 
/// Part of TAL-NoiseMaker by Patrick Kunz
/// Copyright (c) 2005-2010 Patrick Kunz, TAL - Togu Audio Line, Inc.
/// Licensed under GNU General Public License v2.0
/// http://kunz.corrupt.ch
/// 
/// The chorus effect is achieved by:
/// 1. Storing input samples in a circular delay buffer
/// 2. Reading from the buffer at a position modulated by an LFO
/// 3. The varying delay time creates pitch modulation (chorus/thickening)
/// 4. Linear interpolation between samples ensures smooth modulation
/// </remarks>
public sealed class Chorus : IDisposable {
    private readonly float _sampleRate;
    private readonly float _delayTime;
    private readonly Lfo _lfo;
    private readonly OnePoleLP _lp;

    private readonly int _delayLineLength;
    private readonly float[] _delayLine;
    private int _writeIndex;
    private float _delayLineOutput;

    private readonly float _rate;

    // LFO state (simple triangle oscillator for modulation)
    private float _lfoPhase;
    private readonly float _lfoStepSize;
    private float _lfoSign;

    // Interpolation state
    private float _z1;

    /// <summary>
    /// Initializes a new instance of the <see cref="Chorus"/> class.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="phase">Initial LFO phase (0.0-1.0).</param>
    /// <param name="rate">LFO rate in Hz (modulation speed).</param>
    /// <param name="delayTime">Maximum delay time in milliseconds.</param>
    public Chorus(float sampleRate, float phase, float rate, float delayTime) {
        _sampleRate = sampleRate;
        _delayTime = delayTime;
        _rate = rate;

        // Create LFO for modulation
        _lfo = new Lfo(sampleRate);
        _lfo.ResetPhase(phase);
        _lfo.SetRate(rate);

        // Create lowpass filter for smoothing
        _lp = new OnePoleLP();

        // Compute required buffer size for desired delay
        // Multiply by 2 to allow for modulation range
        _delayLineLength = (int)MathF.Floor(delayTime * sampleRate * 0.001f) * 2;

        // Allocate delay line buffer
        _delayLine = new float[_delayLineLength];

        // Initialize state
        _writeIndex = 0;
        _delayLineOutput = 0.0f;
        _z1 = 0.0f;

        // Initialize simple triangle LFO
        _lfoPhase = phase * 2.0f - 1.0f; // Convert 0-1 to -1 to +1
        _lfoStepSize = 4.0f * rate / sampleRate;
        _lfoSign = 1.0f;

        // Zero out the buffer (silence)
        Array.Clear(_delayLine, 0, _delayLineLength);
    }

    /// <summary>
    /// Processes a single sample through the chorus effect.
    /// </summary>
    /// <param name="sample">Input sample (read-only, input is written to delay line).</param>
    /// <returns>The chorus effect output (delayed and modulated sample).</returns>
    public float Process(float sample) {
        // Get modulated delay time from LFO
        // Map LFO output [-1,+1] to delay range [0.4*delayTime, 0.7*delayTime]
        float offset = (NextLfo() * 0.3f + 0.4f) * _delayTime * _sampleRate * 0.001f;

        // Compute read pointer based on offset
        // Read from (write position - offset) samples ago
        int readOffset = (int)MathF.Floor(offset);
        int readPos = _writeIndex - readOffset;

        // Wrap around if before start of buffer
        if (readPos < 0) {
            readPos += _delayLineLength;
        }

        // Get two adjacent samples for linear interpolation
        float sample1 = _delayLine[readPos];
        
        int readPos2 = readPos - 1;
        if (readPos2 < 0) {
            readPos2 += _delayLineLength;
        }
        float sample2 = _delayLine[readPos2];

        // Linear interpolation between samples
        float frac = offset - (int)MathF.Floor(offset);
        _delayLineOutput = sample2 + sample1 * (1.0f - frac) - (1.0f - frac) * _z1;
        _z1 = _delayLineOutput;

        // Apply lowpass filter to smooth the output
        _lp.Tick(ref _delayLineOutput, 0.95f);

        // Write input sample to delay line
        _delayLine[_writeIndex] = sample;

        // Increment write index and wrap if necessary
        _writeIndex++;
        if (_writeIndex >= _delayLineLength) {
            _writeIndex = 0;
        }

        return _delayLineOutput;
    }

    /// <summary>
    /// Generates next LFO value using simple triangle wave oscillator.
    /// This is a lightweight alternative to the full Lfo class for internal modulation.
    /// </summary>
    /// <returns>LFO value in range [-1.0, +1.0].</returns>
    private float NextLfo() {
        // Reverse direction at boundaries
        if (_lfoPhase >= 1.0f) {
            _lfoSign = -1.0f;
        } else if (_lfoPhase <= -1.0f) {
            _lfoSign = +1.0f;
        }

        // Increment phase
        _lfoPhase += _lfoStepSize * _lfoSign;

        return _lfoPhase;
    }

    /// <summary>
    /// Disposes resources used by the Chorus instance.
    /// </summary>
    public void Dispose() {
        // Delay line is managed by GC, no explicit cleanup needed
        // This method exists for consistency with C++ destructor pattern
    }
}
