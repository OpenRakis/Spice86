using System;

namespace Bufdio.Spice86.Engines;

/// <summary>
/// An interface to interact with the output audio device.
/// <para>Implements: <see cref="IDisposable"/>.</para>
/// </summary>
public interface IAudioEngine : IDisposable {
    /// <summary>
    /// Sends audio samples to the output device.
    /// </summary>
    /// <param name="frames">Audio samples in <c>Float32</c> format.</param>
    void Send(Span<float> frames);

    /// <summary>
    /// Starts audio playback. Matches DOSBox behavior where SDL audio starts paused
    /// and is unpaused via SDL_PauseAudioDevice when ready.
    /// Reference: DOSBox mixer.cpp - "An opened audio device starts out paused"
    /// </summary>
    void Start();
}
