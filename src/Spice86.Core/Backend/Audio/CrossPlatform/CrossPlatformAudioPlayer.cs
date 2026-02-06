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

    /// <summary>
    /// Creates a new cross-platform audio player.
    /// </summary>
    /// <param name="format">Audio format.</param>
    /// <param name="backend">Platform-specific audio backend.</param>
    /// <param name="bufferFrames">Buffer size in frames for the audio device (blocksize).</param>
    public CrossPlatformAudioPlayer(AudioFormat format, IAudioBackend backend, int bufferFrames)
        : base(format) {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

        // Calculate queue capacity like DOSBox: blocksize + prebuffer_frames
        // Reference: const auto prebuffer_frames = (mixer.sample_rate_hz * mixer.prebuffer_ms) / 1000;
        // Reference: mixer.final_output.Resize(mixer.blocksize + prebuffer_frames);
        const int DefaultPrebufferMs = 25;
        int prebufferFrames = format.SampleRate * DefaultPrebufferMs / 1000;
        int queueFrames = bufferFrames + prebufferFrames;

        // Convert frames to floats (stereo = 2 channels)
        _queueCapacity = queueFrames * format.Channels;
        _ringBuffer = new float[_queueCapacity];

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
    /// This runs on the audio thread. Non-blocking - fills with silence if underrun.
    /// Reference: DOSBox mixer_callback() - dequeues available frames, fills shortfall with silence
    /// Reference: rwqueue.h BulkDequeue - non-blocking variant for callbacks
    /// </summary>
    private void AudioCallback(Span<float> buffer) {
        int samplesNeeded = buffer.Length;
        int samplesWritten = 0;

        // Pull samples from the queue - non-blocking dequeue
        // Reference: const auto frames_to_dequeue = std::min(mixer.final_output.Size(), frames_requested);
        // Reference: mixer.final_output.BulkDequeue(frame_stream, frames_to_dequeue);
        lock (_queueLock) {
            int samplesToRead = Math.Min(samplesNeeded, _count);
            for (int i = 0; i < samplesToRead; i++) {
                buffer[samplesWritten++] = _ringBuffer[_readIndex];
                _readIndex = (_readIndex + 1) % _queueCapacity;
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
    internal override int WriteData(Span<float> data) {
        // BulkEnqueue - blocks until space is available
        // Reference: rwqueue.h BulkEnqueue - blocks producer when queue is at capacity
        // Reference: "blocks both the producer until space is available"
        int written = 0;

        lock (_queueLock) {
            foreach (float sample in data) {
                // Wait while queue is full - this is the key DOSBox RWQueue behavior
                // Reference: condition_variable has_room - producer waits for space
                while (_count >= _queueCapacity && _isRunning) {
                    Monitor.Wait(_queueLock);
                }

                if (!_isRunning) {
                    break;
                }

                _ringBuffer[_writeIndex] = sample;
                _writeIndex = (_writeIndex + 1) % _queueCapacity;
                _count++;
                written++;
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
