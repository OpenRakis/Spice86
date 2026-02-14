namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Mac;

using System;
using System.Runtime.Versioning;

using Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Mac.CoreAudio;

/// <summary>
/// SDL audio backend for macOS using CoreAudio (AudioQueue).
/// Reference: DOSBox Staging's SDL audio integration (mixer.cpp)
/// 
/// NOTE: CoreAudio uses ProvidesOwnCallbackThread. The AudioQueue manages
/// its own callback thread via CFRunLoop, so the SdlAudioDevice thread
/// is mostly idle. The outputCallback (in SdlCoreAudioDriver) directly
/// fills audio buffers from the user callback.
/// </summary>
[SupportedOSPlatform("osx")]
public sealed class SdlMacBackend : IAudioBackend {
    private SdlAudioDevice _device;
    private AudioDeviceState _state = AudioDeviceState.Stopped;
    private string? _lastError;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlMacBackend"/> class.
    /// </summary>
    public SdlMacBackend() {
        _device = new SdlAudioDevice(new SdlCoreAudioDriver());
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
