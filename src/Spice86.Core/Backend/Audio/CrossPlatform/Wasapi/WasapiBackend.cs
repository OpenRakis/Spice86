namespace Spice86.Core.Backend.Audio.CrossPlatform.Wasapi;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

/// <summary>
/// WASAPI audio backend for Windows.
/// Implements callback-based audio output using Windows Audio Session API.
/// Reference: SDL's SDL_wasapi.c implementation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiBackend : IAudioBackend {
    private const uint ClsctxAll = 0x17;
    private const int ReftimesPerSec = 10_000_000;
    private const int ReftimesPerMillisec = 10_000;

    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioRenderClient? _renderClient;
    private Thread? _audioThread;
    private ManualResetEventSlim? _stopEvent;
    private AutoResetEvent? _bufferEvent;
    private volatile bool _isRunning;
    private volatile bool _isPaused = true;
    private uint _bufferFrameCount;
    private AudioSpec _obtainedSpec = new AudioSpec();
    private AudioDeviceState _state = AudioDeviceState.Stopped;
    private string? _lastError;
    private AudioCallback? _callback;

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

            // Create device enumerator
            Type? mmDeviceEnumeratorType = Type.GetTypeFromCLSID(WasapiGuids.ClsidMmDeviceEnumerator);
            if (mmDeviceEnumeratorType == null) {
                _lastError = "Failed to get MMDeviceEnumerator type";
                return false;
            }

            object? enumeratorObj = Activator.CreateInstance(mmDeviceEnumeratorType);
            if (enumeratorObj is not IMMDeviceEnumerator enumerator) {
                _lastError = "Failed to create MMDeviceEnumerator";
                return false;
            }
            _deviceEnumerator = enumerator;

            // Get default audio endpoint
            int hr = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console, out IMMDevice device);
            if (WasapiResult.Failed(hr)) {
                _lastError = $"Failed to get default audio endpoint: 0x{hr:X8}";
                return false;
            }
            _device = device;

            // Activate audio client
            Guid iidAudioClient = WasapiGuids.IidIaudioClient;
            hr = _device.Activate(ref iidAudioClient, ClsctxAll, IntPtr.Zero, out object audioClientObj);
            if (WasapiResult.Failed(hr) || audioClientObj is not IAudioClient audioClient) {
                _lastError = $"Failed to activate audio client: 0x{hr:X8}";
                return false;
            }
            _audioClient = audioClient;

            // Create format structure
            WaveFormatEx format = WaveFormatEx.CreateIeeeFloat(desiredSpec.SampleRate, desiredSpec.Channels);
            IntPtr formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
            try {
                Marshal.StructureToPtr(format, formatPtr, false);

                // Calculate buffer duration (in 100-nanosecond units)
                long bufferDuration = (long)desiredSpec.BufferFrames * ReftimesPerSec / desiredSpec.SampleRate;
                // Use at least 20ms buffer for stability
                bufferDuration = Math.Max(bufferDuration, 20 * ReftimesPerMillisec);

                // Initialize audio client with event callback
                hr = _audioClient.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                    bufferDuration,
                    0,
                    formatPtr,
                    IntPtr.Zero);

                if (WasapiResult.Failed(hr)) {
                    _lastError = $"Failed to initialize audio client: 0x{hr:X8}";
                    return false;
                }
            } finally {
                Marshal.FreeHGlobal(formatPtr);
            }

            // Get buffer size
            hr = _audioClient.GetBufferSize(out _bufferFrameCount);
            if (WasapiResult.Failed(hr)) {
                _lastError = $"Failed to get buffer size: 0x{hr:X8}";
                return false;
            }

            // Create event for buffer notifications
            _bufferEvent = new AutoResetEvent(false);
            hr = _audioClient.SetEventHandle(_bufferEvent.SafeWaitHandle.DangerousGetHandle());
            if (WasapiResult.Failed(hr)) {
                _lastError = $"Failed to set event handle: 0x{hr:X8}";
                return false;
            }

            // Get render client
            Guid iidRenderClient = WasapiGuids.IidIaudioRenderClient;
            hr = _audioClient.GetService(ref iidRenderClient, out object renderClientObj);
            if (WasapiResult.Failed(hr) || renderClientObj is not IAudioRenderClient renderClient) {
                _lastError = $"Failed to get render client: 0x{hr:X8}";
                return false;
            }
            _renderClient = renderClient;

            // Store obtained spec
            _obtainedSpec = new AudioSpec {
                SampleRate = desiredSpec.SampleRate,
                Channels = desiredSpec.Channels,
                BufferFrames = (int)_bufferFrameCount,
                Callback = desiredSpec.Callback
            };

            _stopEvent = new ManualResetEventSlim(false);
            _state = AudioDeviceState.Stopped;
            return true;
        } catch (COMException ex) {
            _lastError = $"COM exception during Open: {ex.Message} (0x{ex.HResult:X8})";
            _state = AudioDeviceState.Error;
            return false;
        }
    }

    /// <inheritdoc/>
    public void Start() {
        if (_audioClient == null || _renderClient == null || _state == AudioDeviceState.Playing) {
            return;
        }

        _isPaused = false;

        if (_audioThread == null) {
            _isRunning = true;
            _audioThread = new Thread(AudioThreadProc) {
                Name = "WASAPI Audio Thread",
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };
            _audioThread.Start();
        }

        int hr = _audioClient.Start();
        if (WasapiResult.Failed(hr)) {
            _lastError = $"Failed to start audio client: 0x{hr:X8}";
            _state = AudioDeviceState.Error;
            return;
        }

        _state = AudioDeviceState.Playing;
    }

    /// <inheritdoc/>
    public void Pause() {
        if (_audioClient == null || _state != AudioDeviceState.Playing) {
            return;
        }

        _isPaused = true;
        int hr = _audioClient.Stop();
        if (WasapiResult.Failed(hr)) {
            _lastError = $"Failed to stop audio client: 0x{hr:X8}";
        }
        _state = AudioDeviceState.Stopped;
    }

    /// <inheritdoc/>
    public void Close() {
        _isRunning = false;
        _stopEvent?.Set();

        if (_audioThread != null && _audioThread.IsAlive) {
            _audioThread.Join(timeout: TimeSpan.FromSeconds(2));
            _audioThread = null;
        }

        if (_audioClient != null) {
            _audioClient.Stop();
            _audioClient.Reset();
        }

        if (_renderClient != null) {
            Marshal.ReleaseComObject(_renderClient);
            _renderClient = null;
        }

        if (_audioClient != null) {
            Marshal.ReleaseComObject(_audioClient);
            _audioClient = null;
        }

        if (_device != null) {
            Marshal.ReleaseComObject(_device);
            _device = null;
        }

        if (_deviceEnumerator != null) {
            Marshal.ReleaseComObject(_deviceEnumerator);
            _deviceEnumerator = null;
        }

        _bufferEvent?.Dispose();
        _bufferEvent = null;

        _stopEvent?.Dispose();
        _stopEvent = null;

        _state = AudioDeviceState.Stopped;
    }

    /// <inheritdoc/>
    public void Dispose() {
        Close();
    }

    private void AudioThreadProc() {
        // Set thread to use COM MTA
        Thread.CurrentThread.SetApartmentState(ApartmentState.MTA);

        float[] tempBuffer = new float[_obtainedSpec.BufferSamples];

        while (_isRunning) {
            if (_stopEvent == null || _bufferEvent == null) {
                break;
            }

            // Wait for buffer event or stop signal
            int waitResult = WaitHandle.WaitAny(new WaitHandle[] { _stopEvent.WaitHandle, _bufferEvent }, millisecondsTimeout: 100);

            if (!_isRunning || waitResult == 0) {
                break;
            }

            if (_isPaused || _audioClient == null || _renderClient == null) {
                continue;
            }

            try {
                // Get current padding (how many frames are already in the buffer)
                int hr = _audioClient.GetCurrentPadding(out uint paddingFrameCount);
                if (WasapiResult.Failed(hr)) {
                    continue;
                }

                // Calculate frames available
                uint framesAvailable = _bufferFrameCount - paddingFrameCount;
                if (framesAvailable == 0) {
                    continue;
                }

                // Get buffer from WASAPI
                hr = _renderClient.GetBuffer(framesAvailable, out IntPtr dataPtr);
                if (WasapiResult.Failed(hr)) {
                    continue;
                }

                // Request audio data from callback
                int samplesNeeded = (int)framesAvailable * _obtainedSpec.Channels;
                if (tempBuffer.Length < samplesNeeded) {
                    tempBuffer = new float[samplesNeeded];
                }

                Span<float> callbackBuffer = tempBuffer.AsSpan(0, samplesNeeded);
                callbackBuffer.Clear(); // Fill with silence by default
                _callback?.Invoke(callbackBuffer);

                // Copy to WASAPI buffer
                Marshal.Copy(tempBuffer, 0, dataPtr, samplesNeeded);

                // Release buffer
                _renderClient.ReleaseBuffer(framesAvailable, 0);
            } catch (COMException) {
                // Device may have been lost, ignore
            }
        }
    }
}
