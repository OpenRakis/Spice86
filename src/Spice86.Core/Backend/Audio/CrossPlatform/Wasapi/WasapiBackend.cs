namespace Spice86.Core.Backend.Audio.CrossPlatform.Wasapi;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

/// <summary>
/// WASAPI audio backend for Windows.
/// Implements callback-based audio output using Windows Audio Session API.
/// Reference: SDL's SDL_wasapi.c implementation - mirrors it exactly.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiBackend : IAudioBackend {
    private const uint ClsctxAll = 0x17;

    // Wait timeout in milliseconds - matches SDL's 200ms timeout
    private const int WaitTimeoutMs = 200;

    // WAIT_OBJECT_0 and WAIT_TIMEOUT for WaitForSingleObjectEx
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;

    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioRenderClient? _renderClient;
    private Thread? _audioThread;
    private IntPtr _bufferEvent;
    private volatile bool _isRunning;
    private volatile bool _isPaused = true;
    private uint _bufferFrameCount;
    private AudioSpec _obtainedSpec = new AudioSpec();
    private AudioDeviceState _state = AudioDeviceState.Stopped;
    private string? _lastError;
    private AudioCallback? _callback;

    // avrt.dll handles for Pro Audio thread priority - matches SDL
    private IntPtr _avrtHandle;
    private IntPtr _taskHandle;

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

            // Create event for buffer notifications - matches SDL: CreateEvent(NULL, FALSE, FALSE, NULL)
            _bufferEvent = NativeMethods.CreateEventW(IntPtr.Zero, false, false, null);
            if (_bufferEvent == IntPtr.Zero) {
                _lastError = "Failed to create event handle";
                return false;
            }

            // Get mix format from the device
            hr = _audioClient.GetMixFormat(out IntPtr mixFormatPtr);
            if (WasapiResult.Failed(hr) || mixFormatPtr == IntPtr.Zero) {
                _lastError = $"Failed to get mix format: 0x{hr:X8}";
                return false;
            }

            // Create format structure - use IEEE float like SDL
            WaveFormatEx format = WaveFormatEx.CreateIeeeFloat(desiredSpec.SampleRate, desiredSpec.Channels);
            IntPtr formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
            try {
                Marshal.StructureToPtr(format, formatPtr, false);

                // Stream flags matching SDL:
                // AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY
                AudioClientStreamFlags streamFlags =
                    AudioClientStreamFlags.EventCallback |
                    AudioClientStreamFlags.AutoConvertPcm |
                    AudioClientStreamFlags.SrcDefaultQuality;

                // Initialize with 0 buffer duration and periodicity for shared mode - let WASAPI choose
                // Reference: SDL uses IAudioClient_Initialize(client, sharemode, streamflags, 0, 0, waveformat, NULL)
                hr = _audioClient.Initialize(
                    AudioClientShareMode.Shared,
                    streamFlags,
                    0,  // bufferDuration - let WASAPI choose
                    0,  // periodicity - must be 0 for shared mode
                    formatPtr,
                    IntPtr.Zero);

                if (WasapiResult.Failed(hr)) {
                    _lastError = $"Failed to initialize audio client: 0x{hr:X8}";
                    return false;
                }
            } finally {
                Marshal.FreeHGlobal(formatPtr);
                NativeMethods.CoTaskMemFree(mixFormatPtr);
            }

            // Set event handle - matches SDL: IAudioClient_SetEventHandle(client, device->hidden->event)
            hr = _audioClient.SetEventHandle(_bufferEvent);
            if (WasapiResult.Failed(hr)) {
                _lastError = $"Failed to set event handle: 0x{hr:X8}";
                return false;
            }

            // Get buffer size
            hr = _audioClient.GetBufferSize(out _bufferFrameCount);
            if (WasapiResult.Failed(hr)) {
                _lastError = $"Failed to get buffer size: 0x{hr:X8}";
                return false;
            }

            // Get device period to match callback size to period size (like SDL does)
            hr = _audioClient.GetDevicePeriod(out long defaultPeriod, out _);
            if (WasapiResult.Failed(hr)) {
                _lastError = $"Failed to get device period: 0x{hr:X8}";
                return false;
            }

            // Calculate sample frames from period - matches SDL:
            // const float period_millis = default_period / 10000.0f;
            // const float period_frames = period_millis * newspec.freq / 1000.0f;
            float periodMillis = defaultPeriod / 10000.0f;
            float periodFrames = periodMillis * desiredSpec.SampleRate / 1000.0f;
            int sampleFrames = (int)MathF.Ceiling(periodFrames);

            // Clamp to buffer size - matches SDL:
            // if (new_sample_frames > (int) bufsize) { new_sample_frames = (int) bufsize; }
            if (sampleFrames > (int)_bufferFrameCount) {
                sampleFrames = (int)_bufferFrameCount;
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
                BufferFrames = sampleFrames,
                Callback = desiredSpec.Callback
            };

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
                IsBackground = true
            };
            _audioThread.Start();
        }

        // Start audio client - matches SDL: ret = IAudioClient_Start(client);
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

        // Signal the event to wake up the thread
        if (_bufferEvent != IntPtr.Zero) {
            NativeMethods.SetEvent(_bufferEvent);
        }

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

        if (_bufferEvent != IntPtr.Zero) {
            NativeMethods.CloseHandle(_bufferEvent);
            _bufferEvent = IntPtr.Zero;
        }

        _state = AudioDeviceState.Stopped;
    }

    /// <inheritdoc/>
    public void Dispose() {
        Close();
    }

    /// <summary>
    /// Audio thread procedure - mirrors SDL's WASAPI_ThreadInit, WaitDevice, GetDeviceBuf, PlayDevice pattern.
    /// </summary>
    private void AudioThreadProc() {
        // Initialize COM for this thread - matches SDL: WIN_CoInitialize()
        int hr = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_MULTITHREADED);
        bool comInitialized = hr >= 0;

        // Set thread to Pro Audio priority - matches SDL:
        // if (pAvSetMmThreadCharacteristicsW) {
        //     DWORD idx = 0;
        //     device->hidden->task = pAvSetMmThreadCharacteristicsW(L"Pro Audio", &idx);
        // }
        _avrtHandle = NativeMethods.LoadLibraryW("avrt.dll");
        if (_avrtHandle != IntPtr.Zero) {
            IntPtr procAddr = NativeMethods.GetProcAddress(_avrtHandle, "AvSetMmThreadCharacteristicsW");
            if (procAddr != IntPtr.Zero) {
                AvSetMmThreadCharacteristicsWDelegate avSetMmThread =
                    Marshal.GetDelegateForFunctionPointer<AvSetMmThreadCharacteristicsWDelegate>(procAddr);
                uint taskIndex = 0;
                _taskHandle = avSetMmThread("Pro Audio", ref taskIndex);
            }
        }

        // Buffer for callback - exactly sample_frames * channels floats
        // Reference: SDL always uses exactly device->sample_frames
        int sampleFrames = _obtainedSpec.BufferFrames;
        int samplesPerCallback = sampleFrames * _obtainedSpec.Channels;
        float[] tempBuffer = new float[samplesPerCallback];

        while (_isRunning) {
            if (_bufferEvent == IntPtr.Zero) {
                break;
            }

            if (_isPaused || _audioClient == null || _renderClient == null) {
                // Still need to wait on event to avoid busy loop
                NativeMethods.WaitForSingleObjectEx(_bufferEvent, WaitTimeoutMs, false);
                continue;
            }

            // =================================================================
            // WASAPI_WaitDevice - EXACT SDL MIRROR
            // Reference: SDL_wasapi.c WASAPI_WaitDevice() for playback devices
            // =================================================================
            // SDL waits in a loop until padding <= sample_frames
            bool waitSucceeded = false;
            while (_isRunning && !_isPaused) {
                // WaitForSingleObjectEx(device->hidden->event, 200, FALSE)
                uint waitResult = NativeMethods.WaitForSingleObjectEx(_bufferEvent, WaitTimeoutMs, false);

                if (!_isRunning) {
                    break;
                }

                if (waitResult == WaitObject0) {
                    // WAIT_OBJECT_0 - event signaled, check padding
                    // Reference: if (waitResult == WAIT_OBJECT_0) { ... if (padding <= (UINT32)device->sample_frames) { break; } }
                    hr = _audioClient.GetCurrentPadding(out uint padding);
                    if (WasapiResult.Failed(hr)) {
                        // WasapiFailed - device lost or dead
                        break;
                    }

                    // SDL: if (padding <= (UINT32)device->sample_frames) { break; }
                    if (padding <= (uint)sampleFrames) {
                        waitSucceeded = true;
                        break;
                    }
                    // Not enough space yet, continue waiting
                } else if (waitResult == WaitTimeout) {
                    // WAIT_TIMEOUT - just continue the loop
                    continue;
                } else {
                    // Wait failed - matches SDL: IAudioClient_Stop(client); return false;
                    _audioClient?.Stop();
                    _isRunning = false;
                    break;
                }
            }

            if (!waitSucceeded || !_isRunning) {
                continue;
            }

            try {
                // =================================================================
                // WASAPI_GetDeviceBuf - EXACT SDL MIRROR
                // Reference: SDL_wasapi.c WASAPI_GetDeviceBuf()
                // SDL always requests exactly device->sample_frames, NOT available frames
                // =================================================================
                // const HRESULT ret = IAudioRenderClient_GetBuffer(device->hidden->render, device->sample_frames, &buffer);
                hr = _renderClient.GetBuffer((uint)sampleFrames, out IntPtr dataPtr);

                if (hr == WasapiResult.AudioClientEBufferTooLarge) {
                    // AUDCLNT_E_BUFFER_TOO_LARGE - buffer is NULL, go back to WaitDevice
                    // Reference: SDL sets buffer_size = 0 and returns NULL, then loops back
                    continue;
                }

                if (WasapiResult.Failed(hr)) {
                    // Device lost or dead
                    continue;
                }

                // =================================================================
                // Fill buffer via callback - mirrors SDL's internal audio thread loop
                // =================================================================
                Span<float> callbackBuffer = tempBuffer.AsSpan(0, samplesPerCallback);
                callbackBuffer.Clear();  // Silent by default
                _callback?.Invoke(callbackBuffer);

                // Copy float data to WASAPI buffer (IEEE float format)
                Marshal.Copy(tempBuffer, 0, dataPtr, samplesPerCallback);

                // =================================================================
                // WASAPI_PlayDevice - EXACT SDL MIRROR
                // Reference: SDL_wasapi.c WASAPI_PlayDevice()
                // WasapiFailed(device, IAudioRenderClient_ReleaseBuffer(device->hidden->render, device->sample_frames, 0));
                // =================================================================
                _renderClient.ReleaseBuffer((uint)sampleFrames, 0);
            } catch (COMException) {
                // Device may have been lost, ignore and continue
            }
        }

        // ThreadDeinit - matches SDL:
        // if (device->hidden->task && pAvRevertMmThreadCharacteristics) {
        //     pAvRevertMmThreadCharacteristics(device->hidden->task);
        // }
        if (_taskHandle != IntPtr.Zero && _avrtHandle != IntPtr.Zero) {
            IntPtr procAddr = NativeMethods.GetProcAddress(_avrtHandle, "AvRevertMmThreadCharacteristics");
            if (procAddr != IntPtr.Zero) {
                AvRevertMmThreadCharacteristicsDelegate avRevert =
                    Marshal.GetDelegateForFunctionPointer<AvRevertMmThreadCharacteristicsDelegate>(procAddr);
                avRevert(_taskHandle);
            }
            _taskHandle = IntPtr.Zero;
        }

        if (_avrtHandle != IntPtr.Zero) {
            NativeMethods.FreeLibrary(_avrtHandle);
            _avrtHandle = IntPtr.Zero;
        }

        // CoUninitialize - matches SDL: WIN_CoUninitialize()
        if (comInitialized) {
            NativeMethods.CoUninitialize();
        }
    }

    // Delegate for AvSetMmThreadCharacteristicsW
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate IntPtr AvSetMmThreadCharacteristicsWDelegate(string taskName, ref uint taskIndex);

    // Delegate for AvRevertMmThreadCharacteristics
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool AvRevertMmThreadCharacteristicsDelegate(IntPtr taskHandle);

    /// <summary>
    /// Native methods for WASAPI and Windows APIs.
    /// </summary>
    private static class NativeMethods {
        public const uint COINIT_MULTITHREADED = 0x0;

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        public static extern void CoTaskMemFree(IntPtr ptr);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObjectEx(IntPtr hHandle, int dwMilliseconds, bool bAlertable);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hLibModule);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
    }
}
