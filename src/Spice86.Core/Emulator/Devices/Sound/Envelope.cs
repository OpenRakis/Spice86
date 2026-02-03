// SPDX-License-Identifier: GPL-2.0-or-later
// Reference: src/audio/private/envelope.h

namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Libs.Sound.Common;

/// <summary>
/// Audio envelope that applies a step-wise earned-volume envelope.
/// The envelope is "earned" in the sense that the edge is expanded when
/// a sample meets or exceeds it. This helps minimize the impact of
/// unnatural waveforms.
/// </summary>
public sealed class Envelope {
    private readonly string _channelName;
    private bool _isActive;
    private int _expireAfterFrames;
    private int _framesDone;
    private float _edge;
    private float _edgeIncrement;
    private float _edgeLimit;

    public Envelope(string channelName) {
        _channelName = channelName;
        _isActive = false;
    }

    /// <summary>
    /// Processes a frame through the envelope.
    /// When the envelope is fully expanded or has expired, this becomes a null-call.
    /// </summary>
    public void Process(bool isStereo, ref AudioFrame frame) {
        if (!_isActive) {
            return;
        }

        // Check if we've exceeded the expiration period
        if (_framesDone >= _expireAfterFrames) {
            _isActive = false;
            return;
        }

        // Check if we've hit the edge limit
        if (_edge >= _edgeLimit) {
            _isActive = false;
            return;
        }

        // Clamp samples to current envelope edge
        bool leftClamped = ClampSample(ref frame.Left, _edge);
        bool rightClamped = ClampSample(ref frame.Right, _edge);

        // If either sample was clamped, expand the envelope edge
        if (leftClamped || rightClamped) {
            _edge += _edgeIncrement;
        }

        _framesDone++;
    }

    /// <summary>
    /// Updates the envelope with audio stream characteristics.
    /// </summary>
    public void Update(int sampleRateHz, int peakAmplitude, byte expansionPhaseMs, byte expireAfterSeconds) {
        if (sampleRateHz <= 0 || peakAmplitude <= 0 || expansionPhaseMs == 0 || expireAfterSeconds == 0) {
            _isActive = false;
            return;
        }

        _expireAfterFrames = sampleRateHz * expireAfterSeconds;
        _edgeLimit = peakAmplitude;

        int expansionPhaseFrames = (sampleRateHz * expansionPhaseMs) / 1000;
        if (expansionPhaseFrames > 0) {
            _edgeIncrement = (float)peakAmplitude / expansionPhaseFrames;
        } else {
            _edgeIncrement = peakAmplitude;
        }

        _edge = 0.0f;
        _framesDone = 0;
        _isActive = true;
    }

    /// <summary>
    /// Reactivates the envelope for another round of enveloping.
    /// </summary>
    public void Reactivate() {
        _edge = 0.0f;
        _framesDone = 0;
        _isActive = true;
    }

    /// <summary>
    /// Clamps a sample to the current edge value.
    /// Returns true if the sample was clamped.
    /// </summary>
    private bool ClampSample(ref float sample, float nextEdge) {
        if (sample > nextEdge) {
            sample = nextEdge;
            return true;
        }
        if (sample < -nextEdge) {
            sample = -nextEdge;
            return true;
        }
        return false;
    }
}
