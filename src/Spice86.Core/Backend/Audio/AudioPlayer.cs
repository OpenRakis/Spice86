namespace Spice86.Core.Backend.Audio;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Implements a background audio playback stream.
/// </summary>
public abstract partial class AudioPlayer : IDisposable {
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
    }
}

/// <summary>
/// Invoked when an audio buffer needs to be filled for playback.
/// </summary>
/// <param name="buffer">Buffer to write to.</param>
/// <param name="samplesWritten">Must be set to the number of samples written to the buffer.</param>
public delegate void BufferNeededCallback<TSample>(Span<TSample> buffer, out int samplesWritten) where TSample : unmanaged;
