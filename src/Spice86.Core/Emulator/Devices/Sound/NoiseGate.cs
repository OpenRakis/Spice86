// SPDX-License-Identifier: GPL-2.0-or-later
// Reference: src/audio/private/noise_gate.h and noise_gate.cpp

namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Libs.Sound.Common;
using Spice86.Libs.Sound.Filters.IirFilters.Filters.Butterworth;

/// <summary>
/// Implements a simple noise gate that mutes the signal below a given threshold.
/// The release and attack parameters control how quickly will the signal get
/// muted or brought back from the muted state, respectively.
/// </summary>
public sealed class NoiseGate {
    /// <summary>
    /// Provides a delegate to the Process method for compatibility with MixerChannel.
    /// </summary>
    public ProcessorDelegate Processor => new(Process);

    public delegate AudioFrame ProcessorDelegate(AudioFrame input);
    private float _scaleIn;
    private float _scaleOut;
    private float _thresholdValue;
    private float _attackCoeff;
    private float _releaseCoeff;

    // Second-order Butterworth high-pass filter (stereo)
    private readonly HighPass[] _highpassFilter = new HighPass[2];

    // State variables
    private float _seekV;

    public NoiseGate() {
        _highpassFilter[0] = new HighPass();
        _highpassFilter[1] = new HighPass();
    }

    /// <summary>
    /// Configures the noise gate with operating parameters.
    /// </summary>
    /// <param name="sampleRateHz">Sample rate in Hz</param>
    /// <param name="db0fsSampleValue">The 0dBFS sample value (peak amplitude)</param>
    /// <param name="thresholdDb">Threshold in dB below which signal is gated</param>
    /// <param name="attackTimeMs">Attack time in milliseconds</param>
    /// <param name="releaseTimeMs">Release time in milliseconds</param>
    public void Configure(int sampleRateHz, float db0fsSampleValue,
                         float thresholdDb, float attackTimeMs, float releaseTimeMs) {
        if (sampleRateHz <= 0) {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        if (attackTimeMs <= 0.0f) {
            throw new ArgumentOutOfRangeException(nameof(attackTimeMs));
        }
        if (releaseTimeMs <= 0.0f) {
            throw new ArgumentOutOfRangeException(nameof(releaseTimeMs));
        }

        _scaleIn = 1.0f / db0fsSampleValue;
        _scaleOut = db0fsSampleValue;

        _thresholdValue = MathF.Pow(2.0f, thresholdDb / 6.0f);

        float sampleRateHzFloat = sampleRateHz;

        _attackCoeff = 1.0f /
                      MathF.Pow(10.0f, 1000.0f / (attackTimeMs * sampleRateHzFloat));

        _releaseCoeff = 1.0f /
                       MathF.Pow(10.0f, 1000.0f / (releaseTimeMs * sampleRateHzFloat));

        _seekV = 1.0f;

        // High-pass filter to remove DC offset and useless ultra-low frequency
        // rumble that would throw off the threshold detector.
        const int HighpassFrequencyHz = 5;
        foreach (HighPass filter in _highpassFilter) {
            filter.Setup(2, sampleRateHzFloat, HighpassFrequencyHz);
        }
    }

    /// <summary>
    /// Processes an audio frame through the noise gate.
    /// </summary>
    /// <param name="input">Input audio frame</param>
    /// <returns>Processed audio frame with noise gating applied</returns>
    public AudioFrame Process(AudioFrame input) {
        // Scale input to [-1.0, 1.0] range and apply high-pass filter
        // to remove any DC offset.
        float left = _highpassFilter[0].Filter(input.Left * _scaleIn);
        float right = _highpassFilter[1].Filter(input.Right * _scaleIn);

        bool isOpen = MathF.Abs(left) > _thresholdValue ||
                     MathF.Abs(right) > _thresholdValue;

        if (isOpen) {
            // Attack phase
            _seekV = _seekV * _attackCoeff + (1 - _attackCoeff);
        } else {
            // Release phase
            _seekV *= _releaseCoeff;
        }

        float gainScalar = _seekV * _scaleOut;

        return new AudioFrame(left * gainScalar, right * gainScalar);
    }
}
