namespace Spice86.Core.Backend.Audio.CrossPlatform;

using System;

/// <summary>
/// Callback delegate invoked by the audio backend when it needs audio data.
/// Reference: SDL_AudioCallback from SDL_audio.h
/// </summary>
/// <param name="buffer">Buffer to fill with audio samples (interleaved float stereo).</param>
/// <remarks>
/// This callback is called from the audio thread. Implementations must be thread-safe
/// and should not block. Fill the buffer with silence (zeros) if no audio is available.
/// </remarks>
public delegate void AudioCallback(Span<float> buffer);

/// <summary>
/// Optional post-mix callback invoked after device mixing.
/// Mirrors SDL's postmix hook for final adjustments.
/// </summary>
/// <param name="buffer">Buffer with mixed audio samples.</param>
public delegate void AudioPostmixCallback(Span<float> buffer);

/// <summary>
/// Audio format specification matching SDL_AudioSpec.
/// </summary>
public sealed class AudioSpec {
    /// <summary>
    /// Sample rate in Hz (e.g., 48000).
    /// </summary>
    public int SampleRate { get; init; } = 48000;

    /// <summary>
    /// Number of audio channels (1 = mono, 2 = stereo).
    /// </summary>
    public int Channels { get; init; } = 2;

    /// <summary>
    /// Buffer size in sample frames.
    /// </summary>
    public int BufferFrames { get; init; } = 1024;

    /// <summary>
    /// Callback function to provide audio data.
    /// </summary>
    public AudioCallback? Callback { get; init; }

    /// <summary>
    /// Optional post-mix callback invoked after device mixing.
    /// </summary>
    public AudioPostmixCallback? PostmixCallback { get; init; }

    /// <summary>
    /// Gets the buffer size in samples (frames * channels).
    /// </summary>
    public int BufferSamples => BufferFrames * Channels;

    /// <summary>
    /// Gets the buffer size in bytes (for float samples).
    /// </summary>
    public int BufferBytes => BufferSamples * sizeof(float);
}

/// <summary>
/// Audio device state.
/// </summary>
public enum AudioDeviceState {
    /// <summary>
    /// Device is stopped/paused.
    /// </summary>
    Stopped,

    /// <summary>
    /// Device is playing audio.
    /// </summary>
    Playing,

    /// <summary>
    /// Device encountered an error.
    /// </summary>
    Error
}
