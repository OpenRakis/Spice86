namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl;

using System;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// SDL2 audio backend for Linux and macOS.
/// Implements callback-based audio output using SDL2's audio subsystem.
/// Reference: DOSBox Staging's SDL audio implementation.
/// </summary>
public sealed class SdlBackend : IAudioBackend {
    private uint _deviceId;
    private SdlAudioCallback? _nativeCallback;
    private GCHandle _callbackHandle;
    private AudioSpec _obtainedSpec = new();
    private AudioDeviceState _state = AudioDeviceState.Stopped;
    private string? _lastError;
    private AudioCallback? _callback;
    private AudioPostmixCallback? _postmixCallback;
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public AudioSpec ObtainedSpec => _obtainedSpec;

    /// <inheritdoc/>
    public AudioDeviceState State => _state;

    /// <inheritdoc/>
    public string? LastError => _lastError;

    /// <inheritdoc/>
    public bool Open(AudioSpec desiredSpec) {
        ArgumentNullException.ThrowIfNull(desiredSpec);
        ArgumentNullException.ThrowIfNull(desiredSpec.Callback);

        try {
            _callback = desiredSpec.Callback;
            _postmixCallback = desiredSpec.PostmixCallback;

            // Initialize SDL audio subsystem
            int result = SdlNativeMethods.SdlInit(SdlNativeMethods.SdlInitAudio);
            if (result < 0) {
                _lastError = $"Failed to initialize SDL audio: {SdlNativeMethods.SdlGetError()}";
                return false;
            }

            // Create native callback and prevent GC
            _nativeCallback = NativeAudioCallback;
            _callbackHandle = GCHandle.Alloc(_nativeCallback);

            // Setup desired audio spec
            SdlAudioSpec desired = new SdlAudioSpec {
                Freq = desiredSpec.SampleRate,
                Format = SdlAudioFormat.F32,
                Channels = (byte)desiredSpec.Channels,
                Silence = 0,
                Samples = (ushort)desiredSpec.BufferFrames,
                Callback = Marshal.GetFunctionPointerForDelegate(_nativeCallback),
                Userdata = IntPtr.Zero
            };

            // Open audio device
            _deviceId = SdlNativeMethods.SdlOpenAudioDevice(
                null, // Default device
                0,    // Playback (not capture)
                ref desired,
                out SdlAudioSpec obtained,
                SdlNativeMethods.SdlAudioAllowFrequencyChange | SdlNativeMethods.SdlAudioAllowSamplesChange);

            if (_deviceId == 0) {
                _lastError = $"Failed to open SDL audio device: {SdlNativeMethods.SdlGetError()}";
                if (_callbackHandle.IsAllocated) {
                    _callbackHandle.Free();
                }
                return false;
            }

            // Store obtained spec
            _obtainedSpec = new AudioSpec {
                SampleRate = obtained.Freq,
                Channels = obtained.Channels,
                BufferFrames = obtained.Samples,
                Callback = desiredSpec.Callback,
                PostmixCallback = desiredSpec.PostmixCallback
            };

            _state = AudioDeviceState.Stopped;
            return true;
        } catch (DllNotFoundException) {
            _lastError = "SDL2 library not found";
            _state = AudioDeviceState.Error;
            return false;
        } catch (EntryPointNotFoundException ex) {
            _lastError = $"SDL function not found: {ex.Message}";
            _state = AudioDeviceState.Error;
            return false;
        }
    }

    /// <inheritdoc/>
    public void Start() {
        if (_deviceId == 0 || _state == AudioDeviceState.Playing) {
            return;
        }

        // Unpause the audio device (SDL starts paused by default)
        SdlNativeMethods.SdlPauseAudioDevice(_deviceId, 0);
        _state = AudioDeviceState.Playing;
    }

    /// <inheritdoc/>
    public void Pause() {
        if (_deviceId == 0 || _state != AudioDeviceState.Playing) {
            return;
        }

        SdlNativeMethods.SdlPauseAudioDevice(_deviceId, 1);
        _state = AudioDeviceState.Stopped;
    }

    /// <inheritdoc/>
    public void Close() {
        if (_deviceId != 0) {
            SdlNativeMethods.SdlPauseAudioDevice(_deviceId, 1);
            SdlNativeMethods.SdlCloseAudioDevice(_deviceId);
            _deviceId = 0;
        }

        if (_callbackHandle.IsAllocated) {
            _callbackHandle.Free();
        }

        SdlNativeMethods.SdlQuitSubSystem(SdlNativeMethods.SdlInitAudio);
        _state = AudioDeviceState.Stopped;
    }

    /// <inheritdoc/>
    public void Dispose() {
        Close();
    }

    private void NativeAudioCallback(IntPtr userdata, IntPtr stream, int len) {
        lock (_lock) {
            if (_state != AudioDeviceState.Playing || _callback == null) {
                // Fill with silence
                unsafe {
                    Span<byte> silence = new Span<byte>((void*)stream, len);
                    silence.Clear();
                }
                return;
            }

            // Calculate samples (len is in bytes, we use float samples)
            int samples = len / sizeof(float);

            // Create span over the SDL buffer
            unsafe {
                Span<float> buffer = new Span<float>((void*)stream, samples);
                buffer.Clear(); // Start with silence
                _callback.Invoke(buffer);
                _postmixCallback?.Invoke(buffer);
            }
        }
    }
}
