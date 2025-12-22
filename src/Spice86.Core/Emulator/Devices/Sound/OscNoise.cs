namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Noise oscillator for TAL-Chorus LFO randomization.
/// Implements a simple linear congruential generator (LCG) for audio-rate noise.
/// </summary>
/// <remarks>
/// Ported from DOSBox Staging: /src/libs/tal-chorus/OscNoise.h
/// 
/// Part of TAL-NoiseMaker by Patrick Kunz
/// Copyright (c) 2005-2010 Patrick Kunz, TAL - Togu Audio Line, Inc.
/// Licensed under GNU General Public License v2.0
/// http://kunz.corrupt.ch
/// </remarks>
public sealed class OscNoise {
    private int _randSeed;
    private float _currentValueFiltered;
    private float _valueUnfiltered;

    /// <summary>
    /// Initializes a new instance of the <see cref="OscNoise"/> class.
    /// </summary>
    public OscNoise() {
        ResetOsc();
    }

    /// <summary>
    /// Resets the oscillator to initial state.
    /// </summary>
    public void ResetOsc() {
        _randSeed = 1;
        _currentValueFiltered = 0.0f;
        _valueUnfiltered = 0.0f;
    }

    /// <summary>
    /// Generates the next noise sample using LCG algorithm.
    /// Returns values in range approximately [-1.0, +1.0].
    /// </summary>
    /// <returns>Random noise sample.</returns>
    public float GetNextSample() {
        _randSeed *= 16807;
        // Convert signed 32-bit int to float in [-1.0, +1.0] range
        // 4.6566129e-010f = 1.0 / (2^31)
        return _randSeed * 4.6566129e-010f;
    }

    /// <summary>
    /// Generates the next noise sample in positive range [0.0, 1.0].
    /// </summary>
    /// <returns>Random noise sample (positive only).</returns>
    public float GetNextSamplePositive() {
        _randSeed *= 16807;
        // Mask to keep only positive values (0 to 2^31-1)
        return (_randSeed & 0x7FFFFFFF) * 4.6566129e-010f;
    }

    /// <summary>
    /// Generates the next noise sample with vintage character.
    /// Applies filtering and sample-hold behavior for warmer, less harsh noise.
    /// </summary>
    /// <returns>Filtered random noise sample.</returns>
    public float GetNextSampleVintage() {
        _randSeed *= 16807;
        float oldValue = _valueUnfiltered;
        _valueUnfiltered = _randSeed * 4.6566129e-010f;

        // Sample-hold: if value is too small, use half of previous value
        if (Math.Abs(_valueUnfiltered) < 0.95f) {
            _valueUnfiltered = oldValue * 0.5f;
        }

        // Simple lowpass filter: average with previous 3 samples
        _currentValueFiltered = (_valueUnfiltered + _currentValueFiltered * 3.0f) / 4.0f;
        return _currentValueFiltered;
    }
}
