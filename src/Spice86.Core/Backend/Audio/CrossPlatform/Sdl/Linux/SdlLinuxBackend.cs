namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Linux;

using System;
using System.Runtime.Versioning;

using Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Linux.Alsa;

/// <summary>
/// SDL audio backend for Linux using ALSA.
/// Reference: DOSBox Staging's SDL audio integration (mixer.cpp)
/// - Opens ALSA device via SdlAlsaDriver (matching SDL_alsa_audio.c behavior)
/// - Uses SdlAudioDevice for thread management (matching SDL_RunAudio)
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class SdlLinuxBackend : IAudioBackend {
    private SdlAudioDevice _device;
    private AudioDeviceState _state = AudioDeviceState.Stopped;
    private string? _lastError;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlLinuxBackend"/> class.
    /// </summary>
    public SdlLinuxBackend() {
        _device = new SdlAudioDevice(new SdlAlsaDriver());
    }

    /// <inheritdoc/>
    public AudioSpec ObtainedSpec => _device.ObtainedSpec;

    /// <inheritdoc/>
    public AudioDeviceState State => _state;

    /// <inheritdoc/>
    public string? LastError => _lastError;

    /// <inheritdoc/>
    public bool Open(AudioSpec desiredSpec) {
        ArgumentNullException.ThrowIfNull(desiredSpec);
        ArgumentNullException.ThrowIfNull(desiredSpec.Callback);

        if (!_device.Open(desiredSpec)) {
            _lastError = _device.LastError;
            _state = AudioDeviceState.Error;
            return false;
        }

        _state = AudioDeviceState.Stopped;
        return true;
    }

    /// <inheritdoc/>
    public void Start() {
        if (_state == AudioDeviceState.Playing) {
            return;
        }

        _device.Start();
        _state = AudioDeviceState.Playing;
    }

    /// <inheritdoc/>
    public void Pause() {
        if (_state != AudioDeviceState.Playing) {
            return;
        }

        _device.Pause();
        _state = AudioDeviceState.Stopped;
    }

    /// <inheritdoc/>
    public void Close() {
        _device.Close();
        _state = AudioDeviceState.Stopped;
    }

    /// <inheritdoc/>
    public void Dispose() {
        Close();
    }
}
