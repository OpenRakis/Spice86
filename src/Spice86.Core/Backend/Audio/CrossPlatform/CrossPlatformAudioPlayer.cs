namespace Spice86.Core.Backend.Audio.CrossPlatform;

using System;
using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// Audio player that uses the cross-platform audio backend with callback-based audio.
/// The mixer produces audio frames which are queued and consumed by the audio callback.
/// This mirrors DOSBox Staging's SDL callback model.
/// </summary>
public sealed class CrossPlatformAudioPlayer : AudioPlayer {
    private readonly IAudioBackend _backend;
    private readonly ConcurrentQueue<float> _sampleQueue = new();
    private readonly int _targetQueueSamples;
    private volatile bool _started;

    /// <summary>
    /// Creates a new cross-platform audio player.
    /// </summary>
    /// <param name="format">Audio format.</param>
    /// <param name="backend">Platform-specific audio backend.</param>
    /// <param name="bufferFrames">Buffer size in frames for the audio device.</param>
    public CrossPlatformAudioPlayer(AudioFormat format, IAudioBackend backend, int bufferFrames)
        : base(format) {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

        // Target queue should hold enough samples for smooth playback
        // Use 4x the buffer size to prevent underruns
        _targetQueueSamples = bufferFrames * format.Channels * 4;

        // Configure the audio spec with our callback
        AudioSpec spec = new AudioSpec {
            SampleRate = format.SampleRate,
            Channels = format.Channels,
            BufferFrames = bufferFrames,
            Callback = AudioCallback
        };

        if (!_backend.Open(spec)) {
            throw new InvalidOperationException($"Failed to open audio backend: {_backend.LastError}");
        }
    }

    /// <summary>
    /// Audio callback invoked by the backend when it needs audio data.
    /// This runs on the audio thread.
    /// </summary>
    private void AudioCallback(Span<float> buffer) {
        int samplesNeeded = buffer.Length;
        int samplesWritten = 0;

        // Pull samples from the queue
        while (samplesWritten < samplesNeeded && _sampleQueue.TryDequeue(out float sample)) {
            buffer[samplesWritten++] = sample;
        }

        // Fill remaining with silence if queue underran
        if (samplesWritten < samplesNeeded) {
            buffer.Slice(samplesWritten).Clear();
        }
    }

    /// <inheritdoc/>
    internal override void Start() {
        if (_started) {
            return;
        }
        _started = true;
        _backend.Start();
    }

    /// <inheritdoc/>
    internal override int WriteData(Span<float> data) {
        // Queue the samples for the audio callback to consume
        foreach (float sample in data) {
            _sampleQueue.Enqueue(sample);
        }

        // If queue is getting too large, we need to slow down
        // This provides backpressure similar to blocking writes
        while (_sampleQueue.Count > _targetQueueSamples * 2) {
            Thread.Sleep(1);
        }

        return data.Length;
    }

    /// <summary>
    /// Gets the number of queued samples.
    /// </summary>
    public int QueuedSamples => _sampleQueue.Count;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            _backend.Pause();
            _backend.Close();
            _backend.Dispose();
        }

        base.Dispose(disposing);
    }
}
