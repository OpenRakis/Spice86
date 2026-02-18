namespace Spice86.Audio.Backend.Audio.DummyAudio;

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Audio player that captures all mixer output into memory for analysis.
/// Extends the DummyAudioPlayer behavior (backpressure simulation) while
/// recording every sample written by the mixer thread.
/// </summary>
public sealed class CapturingAudioPlayer : AudioPlayer {
    private readonly List<float> _capturedSamples = new();
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CapturingAudioPlayer"/> class.
    /// </summary>
    /// <param name="format">The audio playback format.</param>
    public CapturingAudioPlayer(AudioFormat format) : base(format) {
    }

    /// <summary>
    /// Writes audio data while capturing it for later analysis.
    /// Yields the current time slice after each write to prevent the mixer
    /// thread from monopolizing CPU time when there is no real audio device.
    /// </summary>
    /// <param name="data">The input audio data (interleaved stereo float samples).</param>
    /// <returns>The data parameter length.</returns>
    public override int WriteData(Span<float> data) {
        lock (_lock) {
            for (int i = 0; i < data.Length; i++) {
                _capturedSamples.Add(data[i]);
            }
        }
        // Yield to prevent mixer thread starvation of the emulation thread.
        // A real audio device blocks here until the callback drains the queue;
        // Thread.Sleep(0) achieves a similar effect by relinquishing the
        // remainder of the current time slice.
        Thread.Sleep(0);
        return data.Length;
    }

    /// <summary>
    /// Gets a copy of all captured audio samples (interleaved stereo).
    /// </summary>
    /// <returns>Array of interleaved float samples [L, R, L, R, ...].</returns>
    public float[] GetCapturedSamples() {
        lock (_lock) {
            return _capturedSamples.ToArray();
        }
    }

    /// <summary>
    /// Gets the total number of captured stereo frames.
    /// </summary>
    public int CapturedFrameCount {
        get {
            lock (_lock) {
                int channels = Format.Channels;
                return channels > 0 ? _capturedSamples.Count / channels : 0;
            }
        }
    }

    /// <inheritdoc/>
    public override void Start() {
    }

    /// <inheritdoc/>
    public override void ClearQueuedData() {
    }

    /// <inheritdoc/>
    public override void MuteOutput() {
    }

    /// <inheritdoc/>
    public override void UnmuteOutput() {
    }
}
