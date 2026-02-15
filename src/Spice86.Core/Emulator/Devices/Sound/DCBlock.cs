namespace Spice86.Core.Emulator.Devices.Sound;

/// <summary>
/// DC blocking filter for TAL-Chorus.
/// Removes DC offset (zero-frequency component) from audio signal using a high-pass filter.
/// </summary>
/// <remarks>
/// Part of TAL-NoiseMaker by Patrick Kunz
/// Copyright (c) 2005-2010 Patrick Kunz, TAL - Togu Audio Line, Inc.
/// Licensed under GNU General Public License v2.0
/// http://kunz.corrupt.ch
/// 
/// The filter is a simple first-order high-pass filter that removes DC bias
/// while allowing AC (audio) signals to pass through.
/// </remarks>
public sealed class DCBlock {
    private float _inputs;
    private float _outputs;
    private float _lastOutput;

    /// <summary>
    /// Initializes a new instance of the <see cref="DCBlock"/> class.
    /// </summary>
    public DCBlock() {
        _inputs = 0.0f;
        _outputs = 0.0f;
        _lastOutput = 0.0f;
    }

    /// <summary>
    /// Processes a single sample through the DC blocking filter.
    /// Updates the sample in-place.
    /// </summary>
    /// <param name="sample">Reference to the sample to process (modified in-place).</param>
    /// <param name="cutoff">Cutoff control (0.0-1.0). Higher values = more DC blocking.</param>
    public void Tick(ref float sample, float cutoff) {
        _outputs = sample - _inputs + (0.999f - cutoff * 0.4f) * _outputs;
        _inputs = sample;
        _lastOutput = _outputs;
        sample = _lastOutput;
    }
}
