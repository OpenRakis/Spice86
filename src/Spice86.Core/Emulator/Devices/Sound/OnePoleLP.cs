namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// Simple one-pole lowpass filter for TAL-Chorus.
/// Provides smooth filtering of high-frequency content.
/// </summary>
/// <remarks>
/// Ported from DOSBox Staging: /src/libs/tal-chorus/OnePoleLP.h
/// 
/// Part of TAL-NoiseMaker by Patrick Kunz
/// Copyright (c) 2005-2010 Patrick Kunz, TAL - Togu Audio Line, Inc.
/// Licensed under GNU General Public License v2.0
/// http://kunz.corrupt.ch
/// 
/// One-pole lowpass is the simplest form of digital lowpass filter.
/// It smooths the signal by averaging current sample with previous output.
/// </remarks>
public sealed class OnePoleLP {
    private float _outputs;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnePoleLP"/> class.
    /// </summary>
    public OnePoleLP() {
        _outputs = 0.0f;
    }

    /// <summary>
    /// Processes a single sample through the lowpass filter.
    /// Updates the sample in-place.
    /// </summary>
    /// <param name="sample">Reference to the sample to process (modified in-place).</param>
    /// <param name="cutoff">Cutoff control (0.0-1.0). Higher values = more smoothing (lower cutoff frequency).</param>
    /// <remarks>
    /// Formula: output = (1-p) * input + p * previous_output
    /// where p = (cutoff * 0.98)^4
    /// The quartic function provides steeper rolloff than linear cutoff control.
    /// </remarks>
    public void Tick(ref float sample, float cutoff) {
        float p = cutoff * 0.98f;
        p = p * p * p * p; // p^4 for steeper response
        _outputs = (1.0f - p) * sample + p * _outputs;
        sample = _outputs;
    }
}
