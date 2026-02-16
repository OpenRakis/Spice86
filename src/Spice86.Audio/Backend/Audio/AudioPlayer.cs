namespace Spice86.Audio.Backend.Audio;

using System;

/// <summary>
/// The base class for all implementations of Audio Players
/// </summary>
public abstract class AudioPlayer : IDisposable {
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
        BufferFrames = 0;
    }

    /// <summary>
    /// Gets the playback audio format.
    /// </summary>
    public AudioFormat Format { get; protected set; }

    /// <summary>
    /// Gets the buffer size in frames for the audio device.
    /// </summary>
    public int BufferFrames { get; protected set; }

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
    /// <returns>The length of data written. Equal to the input data length.</returns>
    public abstract int WriteData(Span<float> data);

    /// <summary>
    /// Starts audio playback.
    /// </summary>
    public abstract void Start();

    /// <summary>
    /// Clears any queued audio data, if supported by the backend.
    /// </summary>
    public abstract void ClearQueuedData();

    /// <summary>
    /// Mutes the audio output at the callback level.
    /// The callback fills with silence regardless of queued data.
    /// </summary>
    public abstract void MuteOutput();

    /// <summary>
    /// Unmutes the audio output at the callback level.
    /// The callback resumes reading from the queue.
    /// </summary>
    public abstract void UnmuteOutput();

    public void WriteSilence() {
        WriteData(new Span<float>([0]));
    }
}