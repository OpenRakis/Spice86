namespace Spice86.Core.Backend.Audio;

using System;

/// <summary>
/// The base class for all implementations of Audio Players
/// </summary>
public abstract class AudioPlayer : IDisposable
{
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
    }

    /// <summary>
    /// Gets the playback audio format.
    /// </summary>
    public AudioFormat Format { get; }

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
    /// <returns>The length of data written. Equals to the input data length.</returns>
    internal abstract int WriteData(Span<float> data);

    internal void WriteSilence() {
        WriteData(new Span<float>([0]));
    }
}