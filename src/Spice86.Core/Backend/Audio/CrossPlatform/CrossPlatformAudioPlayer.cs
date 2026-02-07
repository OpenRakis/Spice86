namespace Spice86.Core.Backend.Audio.CrossPlatform;

using System;
using System.Threading;

/// <summary>
/// Audio player that uses the cross-platform audio backend with callback-based audio.
/// Implements DOSBox Staging's RWQueue semantics: producer blocks until space is available,
/// consumer (callback) dequeues non-blocking and notifies producer when space freed.
/// Reference: DOSBox rwqueue.h - blocking producer-consumer queue
/// Reference: DOSBox mixer.cpp mixer_callback() and BulkEnqueue pattern
/// </summary>
public sealed class CrossPlatformAudioPlayer : AudioPlayer {
    private readonly IAudioBackend _backend;

    // Circular buffer matching DOSBox's RWQueue<AudioFrame>
    // Reference: mixer.final_output.Resize(mixer.blocksize + prebuffer_frames)
    // DOSBox defaults: blocksize=1024, prebuffer_ms=25 → ~2224 frames (~46ms)
    // We use frames * 2 channels to get floats
    private readonly int _queueCapacity;
    private readonly float[] _ringBuffer;
    private int _writeIndex;
    private int _readIndex;
    private int _count;
    private readonly object _queueLock = new();
    private volatile bool _isRunning = true;
    private volatile bool _started;

    // Muted flag - when true, the audio callback outputs silence immediately
    // Reference: SDL_RunAudio checks SDL_AtomicGet(&device->paused) under lock
    // and fills with silence. This provides instant muting for pause.
    private volatile bool _muted;

    /// <summary>
    /// Creates a new cross-platform audio player.
    /// </summary>
    /// <param name="format">Audio format.</param>
    /// <param name="backend">Platform-specific audio backend.</param>
    /// <param name="bufferFrames">Buffer size in frames for the audio device (blocksize).</param>
    /// <param name="prebufferMs">Prebuffer duration in milliseconds.</param>
    public CrossPlatformAudioPlayer(AudioFormat format, IAudioBackend backend, int bufferFrames, int prebufferMs)
        : base(format) {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

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

        AudioSpec obtainedSpec = _backend.ObtainedSpec;
        Format = new AudioFormat(obtainedSpec.SampleRate, obtainedSpec.Channels, format.SampleFormat);
        BufferFrames = obtainedSpec.BufferFrames;

        // Calculate queue capacity like DOSBox: blocksize + prebuffer_frames
        // Reference: const auto prebuffer_frames = (mixer.sample_rate_hz * mixer.prebuffer_ms) / 1000;
        // Reference: mixer.final_output.Resize(mixer.blocksize + prebuffer_frames);
        int prebufferFrames = obtainedSpec.SampleRate * prebufferMs / 1000;
        int queueFrames = obtainedSpec.BufferFrames + prebufferFrames;
        // Use at least 4x blocksize for headroom to prevent underrun clicks
        int minQueueFrames = obtainedSpec.BufferFrames * 4;
        if (queueFrames < minQueueFrames) {
            queueFrames = minQueueFrames;
        }
        // Convert frames to floats (stereo = 2 channels)
        _queueCapacity = queueFrames * obtainedSpec.Channels;
        _ringBuffer = new float[_queueCapacity];
    }

    /// <summary>
    /// Audio callback invoked by the backend when it needs audio data.
    /// This runs on the audio thread. Non-blocking - fills with silence if underrun.
    /// Reference: DOSBox mixer_callback() - dequeues available frames, fills shortfall with silence
    /// Reference: SDL_RunAudio - checks paused flag and fills with silence
    /// Reference: rwqueue.h BulkDequeue - non-blocking variant for callbacks
    /// </summary>
    private void AudioCallback(Span<float> buffer) {
        // Check muted state first - instant silence like SDL's paused check
        // Reference: SDL_RunAudio: if (SDL_AtomicGet(&device->paused)) { SDL_memset(data, silence, data_len); }
        if (_muted) {
            buffer.Clear();
            return;
        }

        int samplesNeeded = buffer.Length;
        int samplesWritten = 0;

        // Pull samples from the queue - non-blocking dequeue
        // Reference: const auto frames_to_dequeue = std::min(mixer.final_output.Size(), frames_requested);
        // Reference: mixer.final_output.BulkDequeue(frame_stream, frames_to_dequeue);
        lock (_queueLock) {
            int samplesToRead = Math.Min(samplesNeeded, _count);
            int remaining = samplesToRead;
            while (remaining > 0) {
                int contiguous = Math.Min(remaining, _queueCapacity - _readIndex);
                _ringBuffer.AsSpan(_readIndex, contiguous).CopyTo(buffer.Slice(samplesWritten, contiguous));
                _readIndex = (_readIndex + contiguous) % _queueCapacity;
                samplesWritten += contiguous;
                remaining -= contiguous;
            }

            _count -= samplesToRead;

            // Signal producer that space is available
            // Reference: RWQueue uses condition_variable has_room to notify waiting producers
            if (samplesToRead > 0) {
                Monitor.PulseAll(_queueLock);
            }
        }

        // Satisfy any shortfall with silence
        // Reference: std::fill(frame_stream + frames_received, frame_stream + frames_requested, AudioFrame{});
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
    internal override void ClearQueuedData() {
        lock (_queueLock) {
            _count = 0;
            _readIndex = _writeIndex;
            Monitor.PulseAll(_queueLock);
        }
    }

    /// <inheritdoc/>
    internal override void MuteOutput() {
        _muted = true;
        // Also clear the queue to avoid stale data on unmute
        // Reference: DOSBox set_mixer_state(Muted) calls mixer.final_output.Clear()
        ClearQueuedData();
    }

    /// <inheritdoc/>
    internal override void UnmuteOutput() {
        _muted = false;
    }

    /// <inheritdoc/>
    internal override int WriteData(Span<float> data) {
        // BulkEnqueue - blocks until space is available
        // Reference: rwqueue.h BulkEnqueue - blocks producer when queue is at capacity
        // Reference: "blocks both the producer until space is available"
        int written = 0;

        lock (_queueLock) {
            int remaining = data.Length;
            while (remaining > 0 && _isRunning) {
                // Wait while queue is full - this is the key DOSBox RWQueue behavior
                // Reference: condition_variable has_room - producer waits for space
                while (_count >= _queueCapacity && _isRunning) {
                    Monitor.Wait(_queueLock);
                }

                if (!_isRunning) {
                    break;
                }

                int space = _queueCapacity - _count;
                int contiguous = Math.Min(space, _queueCapacity - _writeIndex);
                int toCopy = Math.Min(remaining, contiguous);
                data.Slice(written, toCopy).CopyTo(_ringBuffer.AsSpan(_writeIndex, toCopy));
                _writeIndex = (_writeIndex + toCopy) % _queueCapacity;
                _count += toCopy;
                written += toCopy;
                remaining -= toCopy;

            }
        }

        return written;
    }

    /// <summary>
    /// Gets the number of queued samples.
    /// Reference: RWQueue::Size()
    /// </summary>
    public int QueuedSamples {
        get {
            lock (_queueLock) {
                return _count;
            }
        }
    }

    /// <summary>
    /// Stops the queue, unblocking any waiting producers.
    /// Reference: RWQueue::Stop()
    /// </summary>
    private void Stop() {
        lock (_queueLock) {
            _isRunning = false;
            _count = 0;
            _readIndex = _writeIndex;
            Monitor.PulseAll(_queueLock);
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            Stop();
            _backend.Pause();
            _backend.Close();
            _backend.Dispose();
        }

        base.Dispose(disposing);
    }
}
