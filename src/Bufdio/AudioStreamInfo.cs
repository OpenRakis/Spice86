using System;

namespace Bufdio;

/// <summary>
/// Containing audio stream information that is usually retrieved by audio codec.
/// This class cannot be inherited.
/// </summary>
public readonly struct AudioStreamInfo
{
    /// <summary>
    /// Initializes <see cref="AudioStreamInfo"/> structure.
    /// </summary>
    /// <param name="channels">Number of audio channels.</param>
    /// <param name="sampleRate">Audio sample rate.</param>
    /// <param name="duration">Audio stream duration.</param>
    public AudioStreamInfo(int channels, int sampleRate, TimeSpan duration)
    {
        Channels = channels;
        SampleRate = sampleRate;
        Duration = duration;
    }

    /// <summary>
    /// Gets number of audio channels.
    /// </summary>
    public int Channels { get; }

    /// <summary>
    /// Gets audio sample rate.
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// Gets audio stream duration.
    /// </summary>
    public TimeSpan Duration { get; }
}
