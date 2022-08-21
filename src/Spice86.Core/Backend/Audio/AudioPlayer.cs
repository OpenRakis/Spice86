namespace Spice86.Core.Backend.Audio;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Implements a background audio playback stream.
/// </summary>
public abstract class AudioPlayer : IDisposable {
    private readonly InternalBufferWriter _writer;
    private CallbackRaiser? _callbackRaiser;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlayer"/> class.
    /// </summary>
    /// <param name="format">Format of the audio stream.</param>
    protected AudioPlayer(AudioFormat format) {
        Format = format ?? throw new ArgumentNullException(nameof(format));

        _writer = format.SampleFormat switch {
            SampleFormat.UnsignedPcm8 => new InternalBufferWriter<byte>(this),
            SampleFormat.SignedPcm16 => new InternalBufferWriter<short>(this),
            SampleFormat.IeeeFloat32 => new InternalBufferWriter<float>(this),
            _ => throw new ArgumentException("Invalid sample format.")
        };
    }

    /// <summary>
    /// Gets the playback audio format.
    /// </summary>
    public AudioFormat Format { get; }
    /// <summary>
    /// Gets a value indicating whether the player is active.
    /// </summary>
    public bool Playing { get; private set; }

    /// <summary>
    /// Begins playback of the background stream of 32-bit IEEE floating point data.
    /// </summary>
    /// <param name="callback">Delegate invoked when more data is needed for the playback buffer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The stream is already playing.</exception>
    /// <exception cref="ObjectDisposedException">The <see cref="AudioPlayer"/> instance has been disposed.</exception>
    public void BeginPlayback(BufferNeededCallback<float> callback) => BeginPlaybackInternal(callback);

    /// <summary>
    /// Begins playback of the background stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">The stream is already playing.</exception>
    /// <exception cref="ObjectDisposedException">The <see cref="AudioPlayer"/> instance has been disposed.</exception>
    public void BeginPlayback() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(AudioPlayer));
        }

        if (Playing) {
            throw new InvalidOperationException("Playback has already started.");
        }

        _callbackRaiser = null;
        Playing = true;
        Start(false);
    }
    /// <summary>
    /// Stops audio playback if it is currently playing.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The <see cref="AudioPlayer"/> instance has been disposed.</exception>
    public void StopPlayback() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(AudioPlayer));
        }

        if (Playing) {
            Stop();
            Playing = false;
            _callbackRaiser = null;
        }
    }

    /// <summary>
    /// Writes 32-bit IEEE floating point data to the output buffer.
    /// </summary>
    /// <param name="data">Buffer containing data to write.</param>
    /// <returns>Number of samples actually written to the buffer.</returns>
    public int WriteData(Span<float> data) => _writer.WriteData(data);
    /// <summary>
    /// Writes 16-bit PCM data to the output buffer.
    /// </summary>
    /// <param name="data">Buffer containing data to write.</param>
    /// <returns>Number of samples actually written to the buffer.</returns>
    public int WriteData(Span<short> data) => _writer.WriteData(data);

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        _disposed = true;
    }
    protected abstract void Start(bool useCallback);
    protected abstract void Stop();
    protected abstract int WriteDataInternal(Span<byte> data);

    protected void RaiseCallback(Span<byte> buffer, out int samplesWritten) => RaiseCallbackInternal(buffer, out samplesWritten);
    protected void RaiseCallback(Span<short> buffer, out int samplesWritten) => RaiseCallbackInternal(buffer, out samplesWritten);
    protected void RaiseCallback(Span<float> buffer, out int samplesWritten) => RaiseCallbackInternal(buffer, out samplesWritten);

    private void RaiseCallbackInternal<TInput>(Span<TInput> buffer, out int samplesWritten) where TInput : unmanaged {
        if (_callbackRaiser != null) {
            _callbackRaiser.RaiseCallback(MemoryMarshal.AsBytes(buffer), out samplesWritten);
        } else {
            samplesWritten = 0;
        }
    }
    private void BeginPlaybackInternal<TInput>(BufferNeededCallback<TInput> callback) where TInput : unmanaged {
        if (callback == null) {
            throw new ArgumentNullException(nameof(callback));
        }

        if (_disposed) {
            throw new ObjectDisposedException(nameof(AudioPlayer));
        }

        if (Playing) {
            throw new InvalidOperationException("Playback has already started.");
        }

        _callbackRaiser = Format.SampleFormat switch {
            SampleFormat.UnsignedPcm8 => new CallbackRaiser<TInput, byte>(callback),
            SampleFormat.SignedPcm16 => new CallbackRaiser<TInput, short>(callback),
            SampleFormat.IeeeFloat32 => new CallbackRaiser<TInput, float>(callback),
            _ => throw new ArgumentException("Sample format is not support.")
        };

        Playing = true;
        Start(true);
    }

    private abstract class InternalBufferWriter {
        public abstract int WriteData<TInput>(Span<TInput> data) where TInput : unmanaged;
    }

    private sealed class InternalBufferWriter<TOutput> : InternalBufferWriter
        where TOutput : unmanaged {
        private readonly AudioPlayer player;
        private TOutput[]? conversionBuffer;

        public InternalBufferWriter(AudioPlayer player) => this.player = player;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public override int WriteData<TInput>(Span<TInput> data) {
            // if formats are the same no sample conversion is needed
            if (typeof(TInput) == typeof(TOutput)) {
                return player.WriteDataInternal(data.AsBytes()) / Unsafe.SizeOf<TOutput>();
            }

            int minBufferSize = data.Length;
            if (conversionBuffer == null || conversionBuffer.Length < minBufferSize) {
                Array.Resize(ref conversionBuffer, minBufferSize);
            }

            SampleConverter.InternalConvert<TInput, TOutput>(data, conversionBuffer);
            return player.WriteDataInternal(conversionBuffer.AsSpan(0, data.Length).AsBytes()) / Unsafe.SizeOf<TOutput>();
        }
    }

    private abstract class CallbackRaiser {
        public abstract void RaiseCallback(Span<byte> buffer, out int samplesWritten);
    }

    private sealed class CallbackRaiser<TInput, TOutput> : CallbackRaiser
        where TInput : unmanaged
        where TOutput : unmanaged {
        private readonly BufferNeededCallback<TInput> callback;
        private TInput[]? conversionBuffer;

        public CallbackRaiser(BufferNeededCallback<TInput> callback) {
            this.callback = callback;
        }

        public override void RaiseCallback(Span<byte> buffer, out int samplesWritten) {
            // if formats are the same no sample conversion is needed
            if (typeof(TInput) == typeof(TOutput)) {
                callback(buffer.Cast<byte, TInput>(), out samplesWritten);
            } else {
                int minBufferSize = buffer.Length / Unsafe.SizeOf<TOutput>();
                if (conversionBuffer == null || conversionBuffer.Length < minBufferSize) {
                    Array.Resize(ref conversionBuffer, minBufferSize);
                }

                callback(conversionBuffer.AsSpan(0, minBufferSize), out samplesWritten);
                SampleConverter.InternalConvert(conversionBuffer.AsSpan(0, minBufferSize), buffer.Cast<byte, TOutput>());
            }
        }
    }
}

/// <summary>
/// Invoked when an audio buffer needs to be filled for playback.
/// </summary>
/// <param name="buffer">Buffer to write to.</param>
/// <param name="samplesWritten">Must be set to the number of samples written to the buffer.</param>
public delegate void BufferNeededCallback<TSample>(Span<TSample> buffer, out int samplesWritten) where TSample : unmanaged;
