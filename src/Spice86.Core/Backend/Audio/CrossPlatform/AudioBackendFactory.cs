namespace Spice86.Core.Backend.Audio.CrossPlatform;

using System;
using System.Runtime.Versioning;

using Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Linux;
using Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Mac;
using Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows;

/// <summary>
/// Factory for creating platform-specific audio backends.
/// Each platform has a native C# port of the corresponding SDL2 audio driver:
/// - Windows: WASAPI (primary) + DirectSound (fallback)
/// - Linux: ALSA
/// - macOS: CoreAudio (AudioQueue)
/// </summary>
public static class AudioBackendFactory {
    /// <summary>
    /// Creates the appropriate audio backend for the current platform.
    /// </summary>
    /// <returns>An audio backend, or null if no backend is available.</returns>
    public static IAudioBackend? Create() {
        if (OperatingSystem.IsWindows()) {
            return CreateSdlWindowsBackend();
        }

        if (OperatingSystem.IsLinux()) {
            return CreateSdlLinuxBackend();
        }

        if (OperatingSystem.IsMacOS()) {
            return CreateSdlMacBackend();
        }

        return null;
    }

    /// <summary>
    /// Creates the appropriate audio backend for the current platform,
    /// or a dummy backend if no real backend is available.
    /// </summary>
    /// <returns>An audio backend (never null).</returns>
    public static IAudioBackend CreateOrDummy() {
        IAudioBackend? backend = Create();
        return backend ?? new DummyAudioBackend();
    }

    /// <summary>
    /// Tries to create the SDL Windows backend (WASAPI).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static IAudioBackend? CreateSdlWindowsBackend() {
        try {
            return new SdlWindowsBackend();
        } catch (InvalidOperationException) {
            return null;
        }
    }

    /// <summary>
    /// Tries to create the SDL Linux backend (ALSA).
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static IAudioBackend? CreateSdlLinuxBackend() {
        try {
            return new SdlLinuxBackend();
        } catch (DllNotFoundException) {
            return null;
        }
    }

    /// <summary>
    /// Tries to create the SDL macOS backend (CoreAudio).
    /// </summary>
    [SupportedOSPlatform("osx")]
    private static IAudioBackend? CreateSdlMacBackend() {
        try {
            return new SdlMacBackend();
        } catch (DllNotFoundException) {
            return null;
        }
    }
}

/// <summary>
/// Dummy audio backend that does nothing. Used when no real audio backend is available.
/// </summary>
public sealed class DummyAudioBackend : IAudioBackend {
    private AudioSpec _obtainedSpec = new();

    /// <inheritdoc/>
    public AudioSpec ObtainedSpec => _obtainedSpec;

    /// <inheritdoc/>
    public AudioDeviceState State { get; private set; } = AudioDeviceState.Stopped;

    /// <inheritdoc/>
    public string? LastError => null;

    /// <inheritdoc/>
    public bool Open(AudioSpec desiredSpec) {
        _obtainedSpec = desiredSpec;
        return true;
    }

    /// <inheritdoc/>
    public void Start() {
        State = AudioDeviceState.Playing;
    }

    /// <inheritdoc/>
    public void Pause() {
        State = AudioDeviceState.Stopped;
    }

    /// <inheritdoc/>
    public void Close() {
        State = AudioDeviceState.Stopped;
    }

    /// <inheritdoc/>
    public void Dispose() {
        Close();
    }
}
