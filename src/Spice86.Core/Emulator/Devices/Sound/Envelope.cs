// SPDX-License-Identifier: GPL-2.0-or-later

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

        // Only start the envelope once our samples have actual values
        if (frame.Left == 0.0f && _framesDone == 0) {
            return;
        }

        // beyond the edge is the lip. Do any samples walk out onto the lip?
        float lip = _edge + _edgeIncrement;
        bool onLip = ClampSample(ref frame.Left, lip) ||
                     (isStereo && ClampSample(ref frame.Right, lip));

        // If any of the samples are out on the lip, then march the edge forward
        if (onLip) {
            _edge += _edgeIncrement;
        }

        // Should we deactivate the envelope?
        if (++_framesDone > _expireAfterFrames || _edge >= _edgeLimit) {
            _isActive = false;
        }
    }

    /// <summary>
    /// Updates the envelope with audio stream characteristics.
    /// </summary>
    public void Update(int sampleRateHz, int peakAmplitude, byte expansionPhaseMs, byte expireAfterSeconds) {
        if (sampleRateHz == 0 || peakAmplitude == 0 || expansionPhaseMs == 0) {
            return;
        }

        // How many frames should we inspect before expiring?
        _expireAfterFrames = expireAfterSeconds * sampleRateHz;

        // The furtherest allowed edge is the peak sample amplitude.
        _edgeLimit = peakAmplitude;

        // Permit the envelope to achieve peak volume within the expansion_phase
        // (in ms) if the samples happen to constantly press on the edges.
        // ceil_sdivide(a, b) = (a + b - 1) / b
        int expansionPhaseFrames = ((sampleRateHz * expansionPhaseMs) + 999) / 1000;

        // Calculate how much the envelope's edge will grow after a frame
        // presses against it.
        _edgeIncrement = (peakAmplitude + expansionPhaseFrames - 1) / expansionPhaseFrames;

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

    private bool ClampSample(ref float sample, float lip) {
        if (MathF.Abs(sample) > _edge) {
            sample = Math.Clamp(sample, -lip, lip);
            return true;
        }
        return false;
    }
}
