namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl;

using System;

/// <summary>
/// Platform-specific SDL audio driver interface.
/// Reference: SDL_AudioDriverImpl from SDL_sysaudio.h
/// Each platform (WASAPI, DirectSound, ALSA, CoreAudio) implements this interface.
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
    bool WaitDevice(SdlAudioDevice device);

    /// <summary>
    /// Gets a buffer pointer to fill with audio data.
    /// Reference: SDL_AudioDriverImpl.GetDeviceBuf
    /// </summary>
    IntPtr GetDeviceBuffer(SdlAudioDevice device, out int bufferBytes);

    /// <summary>
    /// Submits the filled buffer to the device for playback.
    /// Reference: SDL_AudioDriverImpl.PlayDevice
    /// </summary>
    bool PlayDevice(SdlAudioDevice device, IntPtr buffer, int bufferBytes);

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
