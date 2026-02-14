namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows;

using System;
using System.Runtime.Versioning;

using Spice86.Core.Backend.Audio.CrossPlatform.Sdl;
using Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows.DirectSound;
using Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows.Wasapi;

/// <summary>
/// SDL audio backend for Windows, implemented in C# to match SDL behavior.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SdlWindowsBackend : IAudioBackend {
    private readonly SdlAudioDevice _device;
    private AudioDeviceState _state = AudioDeviceState.Stopped;
    private string? _lastError;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlWindowsBackend"/> class.
    /// </summary>
    public SdlWindowsBackend() {
        _device = new SdlAudioDevice(new SdlWasapiDriver());
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
