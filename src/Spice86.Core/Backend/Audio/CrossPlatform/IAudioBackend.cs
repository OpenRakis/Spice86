namespace Spice86.Core.Backend.Audio.CrossPlatform;

using System;

/// <summary>
/// Cross-platform audio backend interface implementing callback-based audio output.
/// This mirrors SDL's audio subsystem design where the audio thread pulls data via callbacks.
/// </summary>
/// <remarks>
/// Reference: SDL_audio.h (SDL_OpenAudioDevice, SDL_PauseAudioDevice)
/// 
/// The callback model ensures audio timing is controlled by the audio hardware,
/// not by the emulator's render loop. This prevents audio glitches when emulation
/// runs slower or faster than real-time.
/// </remarks>
public interface IAudioBackend : IDisposable {
    /// <summary>
    /// Gets the actual audio specification that was obtained from the audio device.
    /// May differ from the requested spec based on hardware capabilities.
    /// </summary>
    AudioSpec ObtainedSpec { get; }

    /// <summary>
    /// Gets the current state of the audio device.
    /// </summary>
    AudioDeviceState State { get; }

    /// <summary>
    /// Gets the last error message, if any.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Opens the audio device with the specified parameters.
    /// </summary>
    /// <param name="desiredSpec">Desired audio specification.</param>
    /// <returns>True if the device was opened successfully.</returns>
    bool Open(AudioSpec desiredSpec);

    /// <summary>
    /// Starts audio playback. The callback will begin receiving requests for audio data.
    /// Reference: SDL_PauseAudioDevice(device, 0)
    /// </summary>
    void Start();

    /// <summary>
    /// Pauses audio playback. The callback will stop being invoked.
    /// Reference: SDL_PauseAudioDevice(device, 1)
    /// </summary>
    void Pause();

    /// <summary>
    /// Closes the audio device and releases resources.
    /// </summary>
    void Close();
}
