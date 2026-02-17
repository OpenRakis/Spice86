namespace Spice86.Audio.Backend.Audio.CrossPlatform.Sdl;

using System;

/// <summary>
/// Platform-specific SDL audio driver interface.
/// Reference: SDL_AudioDriverImpl from SDL_sysaudio.h
/// Each platform (WASAPI, ALSA, CoreAudio) implements this interface.
/// </summary>
internal interface ISdlAudioDriver {
    /// <summary>
    /// Opens the audio device with the desired spec.
    /// Reference: SDL_AudioDriverImpl.OpenDevice
    /// </summary>
    bool OpenDevice(SdlAudioDevice device, AudioSpec desiredSpec, out AudioSpec obtainedSpec, out int sampleFrames, out string? error);

    /// <summary>
    /// Closes the audio device and releases resources.
    /// Reference: SDL_AudioDriverImpl.CloseDevice
    /// </summary>
    void CloseDevice(SdlAudioDevice device);

    /// <summary>
    /// Waits until the device is ready for more data.
    /// Reference: SDL_AudioDriverImpl.WaitDevice
    /// </summary>
    void WaitDevice(SdlAudioDevice device);

    /// <summary>
    /// Gets a buffer pointer to fill with audio data.
    /// Reference: SDL_AudioDriverImpl.GetDeviceBuf
    /// </summary>
    IntPtr GetDeviceBuf(SdlAudioDevice device);

    /// <summary>
    /// Submits the filled buffer to the device for playback.
    /// Reference: SDL_AudioDriverImpl.PlayDevice
    /// </summary>
    void PlayDevice(SdlAudioDevice device);

    /// <summary>
    /// Called at the start of the audio thread.
    /// Reference: SDL_AudioDriverImpl.ThreadInit
    /// </summary>
    void ThreadInit(SdlAudioDevice device);

    /// <summary>
    /// Called at the end of the audio thread.
    /// Reference: SDL_AudioDriverImpl.ThreadDeinit
    /// </summary>
    void ThreadDeinit(SdlAudioDevice device);
}
