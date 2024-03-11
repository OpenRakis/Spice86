namespace Spice86.Core.Backend.Audio;

using Spice86.Shared.Emulator.Audio;

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

        _writer = new InternalBufferWriter(this);
    }

    /// <summary>
    /// Gets the playback audio format.
    /// </summary>
    public AudioFormat Format { get; }

    /// <summary>
    /// Writes audio data to the output buffer.
    /// </summary>
    /// <param name="data">Buffer containing data to write.</param>
    /// <returns>Number of samples actually written to the buffer.</returns>
    public int WriteData<T>(AudioFrame<T> data) where T : unmanaged {
        return _writer.WriteData(data);
    }

    /// <inheritdoc />
    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    /// <param name="disposing">Whether we are disposing of managed resources.</param>
    protected virtual void Dispose(bool disposing) {
        _disposed = true;
    }

    /// <summary>
    /// Writes the audio data to the rendering backend
    /// </summary>
    /// <param name="data">The data to write</param>
    /// <returns>The length of data written. Might not be equal to the input data length.</returns>
    protected abstract int WriteDataInternal(AudioFrame<float> data);

    private sealed class InternalBufferWriter {
        private readonly AudioPlayer _player;
        private float[]? _conversionBuffer;

        public InternalBufferWriter(AudioPlayer player) => _player = player;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public int WriteData<TInput>(AudioFrame<TInput> input) where TInput : unmanaged {
            Span<TInput> data = new TInput[] { input.Left, input.Right };
            int minBufferSize = data.Length;
            if (_conversionBuffer == null || _conversionBuffer.Length < minBufferSize) {
                Array.Resize(ref _conversionBuffer, minBufferSize);
            }

            SampleConverter.InternalConvert<TInput, float>(data, _conversionBuffer);
            return _player.WriteDataInternal(new AudioFrame<float>() { Left = _conversionBuffer[0], Right = _conversionBuffer[1] });
        }
    }
}