namespace Spice86.Core.Backend.Audio.CrossPlatform;

using System;
using System.Runtime.InteropServices;

using Spice86.Core.Backend.Audio.CrossPlatform.Sdl;
using Spice86.Core.Backend.Audio.CrossPlatform.Wasapi;

/// <summary>
/// Factory for creating platform-specific audio backends.
/// Uses WASAPI on Windows (native C# COM implementation) and SDL on other platforms (Linux, macOS).
/// </summary>
public static class AudioBackendFactory {
    /// <summary>
    /// Creates the appropriate audio backend for the current platform.
    /// </summary>
    /// <returns>An audio backend, or null if no backend is available.</returns>
    public static IAudioBackend? Create() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return CreateWasapiBackend();
        }

        // Linux and macOS use SDL P/Invoke
        return CreateSdlBackend();
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
    /// Tries to create a WASAPI backend on Windows.
    /// Falls back to SDL if WASAPI initialization fails.
    /// </summary>
    private static IAudioBackend? CreateWasapiBackend() {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return CreateSdlBackend();
        }

        try {
            return CreateWasapiBackendInternal();
        } catch (TypeInitializationException) {
            // COM not available, fall back to SDL
            return CreateSdlBackend();
        } catch (InvalidOperationException) {
            // WASAPI initialization failed, fall back to SDL
            return CreateSdlBackend();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IAudioBackend CreateWasapiBackendInternal() {
        return new WasapiBackend();
    }

    /// <summary>
    /// Tries to create an SDL backend.
    /// </summary>
    private static IAudioBackend? CreateSdlBackend() {
        try {
            if (!SdlNativeMethods.Initialize()) {
                return null;
            }
            return new SdlBackend();
        } catch (DllNotFoundException) {
            return null;
        } catch (TypeInitializationException) {
            return null;
        }
    }
}

/// <summary>
/// Dummy audio backend that does nothing. Used when no real audio backend is available.
/// </summary>
public sealed class DummyAudioBackend : IAudioBackend {
    private AudioSpec _obtainedSpec = new AudioSpec();

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
