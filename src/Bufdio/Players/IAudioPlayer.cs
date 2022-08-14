using System;
using System.IO;
using System.Threading.Tasks;
using Bufdio.Common;
using Bufdio.Processors;

namespace Bufdio.Players;

/// <summary>
/// An interface for loading and controlling audio playback.
/// <para>Implements: <see cref="IDisposable"/>.</para>
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>
    /// Event that is raised when player state has been changed.
    /// </summary>
    event EventHandler StateChanged;

    /// <summary>
    /// Event that is raised when player position has been changed.
    /// </summary>
    event EventHandler PositionChanged;

    /// <summary>
    /// Gets whether or not an audio source is loaded and ready for playback.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Gets total duration from loaded audio file.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Gets current player position.
    /// </summary>
    TimeSpan Position { get; }

    /// <summary>
    /// Gets current playback state.
    /// </summary>
    PlaybackState State { get; }

    /// <summary>
    /// Gets whether or not the player is currently seeking an audio stream.
    /// </summary>
    bool IsSeeking { get; }

    /// <summary>
    /// Gets or sets audio volume.
    /// </summary>
    float Volume { get; set; }

    /// <summary>
    /// Gets or sets custom sample processor.
    /// </summary>
    ISampleProcessor CustomSampleProcessor { get; set; }

    /// <summary>
    /// Gets or sets logger instance.
    /// </summary>
    ILogger Logger { get; set; }

    /// <summary>
    /// Loads an audio URL to the player.
    /// </summary>
    /// <param name="url">Audio URL or audio file path.</param>
    /// <returns><c>true</c> if successfully loaded, otherwise, <c>false</c>.</returns>
    Task<bool> LoadAsync(string url);

    /// <summary>
    /// Loads an audio stream to the player.
    /// </summary>
    /// <param name="stream">Source audio stream.</param>
    /// <returns><c>true</c> if successfully loaded, otherwise, <c>false</c>.</returns>
    Task<bool> LoadAsync(Stream stream);

    /// <summary>
    /// Starts audio playback.
    /// </summary>
    void Play();

    /// <summary>
    /// Suspends the player for sending buffers to output device.
    /// </summary>
    void Pause();

    /// <summary>
    /// Stop the playback.
    /// </summary>
    void Stop();

    /// <summary>
    /// Seeks loaded audio to the specified position.
    /// </summary>
    /// <param name="position">Desired seek position.</param>
    void Seek(TimeSpan position);
}
