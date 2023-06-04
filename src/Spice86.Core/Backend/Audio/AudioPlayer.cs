namespace Spice86.Core.Backend.Audio;

using System;
using System.Runtime.CompilerServices;

/// <summary>
/// The base class for the <see cref="PortAudio.PortAudioPlayer" />
/// </summary>
public abstract class AudioPlayer : IDisposable
{
    private readonly InternalBufferWriter _writer;
    
    /// <summary>
    /// Whether the native resources were disposed.
    /// </summary>
    protected bool _disposed;

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

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    /// <param name="disposing">Whether we are disposing of resources.</param>
    protected virtual void Dispose(bool disposing) {
        _disposed = true;
    }
    
    /// <summary>
    /// Writes the audio data to the rendering backend
    /// </summary>
    /// <param name="data">The data to write</param>
    /// <returns>The length of data written. Might not be equal to the input data length.</returns>
    protected abstract int WriteDataInternal(Span<byte> data);

    private sealed class InternalBufferWriter<TOutput> : InternalBufferWriter
        where TOutput : unmanaged {
        private readonly AudioPlayer _player;
        private TOutput[]? _conversionBuffer;

        public InternalBufferWriter(AudioPlayer player) => _player = player;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public override int WriteData<TInput>(Span<TInput> data) {
            // if formats are the same no sample conversion is needed
            if (typeof(TInput) == typeof(TOutput)) {
                return _player.WriteDataInternal(data.AsBytes()) / Unsafe.SizeOf<TOutput>();
            }

            int minBufferSize = data.Length;
            if (_conversionBuffer == null || _conversionBuffer.Length < minBufferSize) {
                Array.Resize(ref _conversionBuffer, minBufferSize);
            }

            SampleConverter.InternalConvert<TInput, TOutput>(data, _conversionBuffer);
            return _player.WriteDataInternal(_conversionBuffer.AsSpan(0, data.Length).AsBytes()) / Unsafe.SizeOf<TOutput>();
        }
    }

    private abstract class InternalBufferWriter {
        public abstract int WriteData<TInput>(Span<TInput> data) where TInput : unmanaged;
    }
}