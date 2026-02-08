namespace Spice86.Core.Backend.Audio.CrossPlatform;

using Spice86.Shared.Utils;

using System;

/// <summary>
/// Audio player that uses the cross-platform audio backend with callback-based audio.
/// Producer (mixer thread) blocks via <see cref="RWQueue{T}.BulkEnqueue(T[], int, int)"/>
/// until space is available; consumer (audio callback) dequeues non-blocking.
/// </summary>
public sealed class CrossPlatformAudioPlayer : AudioPlayer {
    private readonly IAudioBackend _backend;
    private readonly RWQueue<float> _queue;
    private readonly float[] _callbackBuffer;
    private volatile bool _started;
    private volatile bool _muted;

    /// <summary>
    /// Creates a new cross-platform audio player.
    /// </summary>
    /// <param name="format">Audio format.</param>
    /// <param name="backend">Platform-specific audio backend.</param>
    /// <param name="bufferFrames">Buffer size in frames for the audio device.</param>
    /// <param name="prebufferMs">Prebuffer duration in milliseconds.</param>
    public CrossPlatformAudioPlayer(AudioFormat format, IAudioBackend backend, int bufferFrames, int prebufferMs)
        : base(format) {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

        AudioSpec spec = new AudioSpec {
            SampleRate = format.SampleRate,
            Channels = format.Channels,
            BufferFrames = bufferFrames,
            Callback = AudioCallback
        };

        if (!_backend.Open(spec)) {
            throw new InvalidOperationException($"Failed to open audio backend: {_backend.LastError}");
        }

        AudioSpec obtainedSpec = _backend.ObtainedSpec;
        Format = new AudioFormat(obtainedSpec.SampleRate, obtainedSpec.Channels, format.SampleFormat);
        BufferFrames = obtainedSpec.BufferFrames;

        int prebufferFrames = obtainedSpec.SampleRate * prebufferMs / 1000;
        int queueFrames = obtainedSpec.BufferFrames + prebufferFrames;
        int queueCapacity = queueFrames * obtainedSpec.Channels;
        _queue = new RWQueue<float>(queueCapacity);
        _callbackBuffer = new float[queueCapacity];
    }

    /// <summary>
    /// Audio callback invoked by the backend when it needs audio data.
    /// Non-blocking — fills with silence on underrun.
    /// </summary>
    private void AudioCallback(Span<float> buffer) {
        if (_muted) {
            buffer.Clear();
            return;
        }

        int samplesNeeded = buffer.Length;
        int received = _queue.BulkDequeue(_callbackBuffer, samplesNeeded);

        if (received > 0) {
            _callbackBuffer.AsSpan(0, received).CopyTo(buffer);
        }

        if (received < samplesNeeded) {
            buffer.Slice(received).Clear();
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
    internal override void ClearQueuedData() {
        _queue.Clear();
    }

    /// <inheritdoc/>
    internal override void MuteOutput() {
        _muted = true;
        _queue.Clear();
    }

    /// <inheritdoc/>
    internal override void UnmuteOutput() {
        _muted = false;
    }

    /// <inheritdoc/>
    internal override int WriteData(Span<float> data) {
        // BulkEnqueue(ReadOnlySpan<T>) processes in chunks within lock acquisitions,
        // avoiding the need to copy to a temporary array.
        return _queue.BulkEnqueue((ReadOnlySpan<float>)data);
    }

    /// <summary>
    /// Gets the number of queued samples.
    /// </summary>
    public int QueuedSamples => _queue.Size;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            _queue.Stop();
            _backend.Pause();
            _backend.Close();
            _backend.Dispose();
        }

        base.Dispose(disposing);
    }
}
