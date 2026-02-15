namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows.Wasapi;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

using Spice86.Core.Backend.Audio.CrossPlatform.Sdl;

[SupportedOSPlatform("windows")]
internal sealed partial class SdlWasapiDriver : ISdlAudioDriver {
    private const uint ClsctxAll = 0x17;
    private const int WaitTimeoutMs = 200;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;

    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioRenderClient? _renderClient;
    private IntPtr _bufferEvent;
    private int _sampleFrames;
    private int _bufferFrameCount;
    private int _channels;
    private int _bytesPerFrame;

    private IntPtr _avrtHandle;
    private IntPtr _taskHandle;

    public bool OpenDevice(SdlAudioDevice device, AudioSpec desiredSpec, out AudioSpec obtainedSpec, out int sampleFrames, out string? error) {
        obtainedSpec = desiredSpec;
        sampleFrames = 0;
        error = null;

        try {
            Type? enumeratorType = Type.GetTypeFromCLSID(SdlWasapiGuids.ClsidMmDeviceEnumerator);
            if (enumeratorType == null) {
                error = "Failed to get MMDeviceEnumerator type";
                return false;
            }

            object? enumeratorObj = Activator.CreateInstance(enumeratorType);
            if (enumeratorObj is not IMMDeviceEnumerator enumerator) {
                error = "Failed to create MMDeviceEnumerator";
                return false;
            }

            _deviceEnumerator = enumerator;

            int hr = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console, out IMMDevice deviceEndpoint);
            if (SdlWasapiResult.Failed(hr)) {
                error = $"Failed to get default audio endpoint: 0x{hr:X8}";
                return false;
            }

            _device = deviceEndpoint;

            Guid iidAudioClient = SdlWasapiGuids.IidIaudioClient;
            hr = _device.Activate(ref iidAudioClient, ClsctxAll, IntPtr.Zero, out object audioClientObj);
            if (SdlWasapiResult.Failed(hr) || audioClientObj is not IAudioClient audioClient) {
                error = $"Failed to activate audio client: 0x{hr:X8}";
                return false;
            }

            _audioClient = audioClient;

            // Reference: SDL_wasapi.c WASAPI_PrepDevice
            // Create event handle for buffer notifications
            _bufferEvent = NativeMethods.CreateEventW(IntPtr.Zero, false, false, null);
            if (_bufferEvent == IntPtr.Zero) {
                error = "Failed to create event handle";
                return false;
            }

            // Reference: SDL_wasapi.c WASAPI_PrepDevice
            // Get the device's mix format and use it as a base
            IntPtr waveformatPtr = IntPtr.Zero;
            hr = _audioClient.GetMixFormat(out waveformatPtr);
            if (SdlWasapiResult.Failed(hr) || waveformatPtr == IntPtr.Zero) {
                error = $"Failed to get mix format: 0x{hr:X8}";
                return false;
            }

            long defaultPeriod = 0;

            try {
                // Reference: SDL_wasapi.c line ~444
                // this->spec.channels = (Uint8)waveformat->nChannels;
                // SDL adopts the device's native channel count
                WaveFormatEx waveformat = Marshal.PtrToStructure<WaveFormatEx>(waveformatPtr);
                int deviceChannels = waveformat.Channels;

                // Reference: SDL_wasapi.c WASAPI_PrepDevice
                // GetDevicePeriod is called before Initialize in SDL
                hr = _audioClient.GetDevicePeriod(out defaultPeriod, out _);
                if (SdlWasapiResult.Failed(hr)) {
                    error = $"Failed to get device period: 0x{hr:X8}";
                    return false;
                }

                // Reference: SDL_wasapi.c lines ~466-472
                // Favor WASAPI's resampler over our own.
                // Only add AutoConvertPcm + SrcDefaultQuality when sample rate differs.
                // Modify the mix format's sample rate in-place.
                AudioClientStreamFlags streamflags = AudioClientStreamFlags.None;
                if (desiredSpec.SampleRate != (int)waveformat.SamplesPerSec) {
                    streamflags |= AudioClientStreamFlags.AutoConvertPcm |
                                   AudioClientStreamFlags.SrcDefaultQuality;
                    waveformat.SamplesPerSec = (uint)desiredSpec.SampleRate;
                    waveformat.AvgBytesPerSec = waveformat.SamplesPerSec * waveformat.Channels * (ushort)(waveformat.BitsPerSample / 8);
                    Marshal.StructureToPtr(waveformat, waveformatPtr, false);
                }

                streamflags |= AudioClientStreamFlags.EventCallback;
                hr = _audioClient.Initialize(AudioClientShareMode.Shared, streamflags, 0, 0, waveformatPtr, IntPtr.Zero);
                if (SdlWasapiResult.Failed(hr)) {
                    error = $"Failed to initialize audio client: 0x{hr:X8}";
                    return false;
                }

                // Store adopted channel count for later use
                _channels = deviceChannels;
            } finally {
                NativeMethods.CoTaskMemFree(waveformatPtr);
            }

            hr = _audioClient.SetEventHandle(_bufferEvent);
            if (SdlWasapiResult.Failed(hr)) {
                error = $"Failed to set event handle: 0x{hr:X8}";
                return false;
            }

            hr = _audioClient.GetBufferSize(out uint bufferFrameCount);
            if (SdlWasapiResult.Failed(hr)) {
                error = $"Failed to get buffer size: 0x{hr:X8}";
                return false;
            }

            // Reference: SDL_wasapi.c WASAPI_PrepDevice lines ~490-497
            // Match the callback size to the period size to cut down on
            // the number of interrupts waited for in each call to WaitDevice
            float periodMillis = defaultPeriod / 10000.0f;
            float periodFrames = periodMillis * desiredSpec.SampleRate / 1000.0f;
            int calculatedFrames = (int)MathF.Ceiling(periodFrames);

            // Regardless of what we calculated for the period size, clamp it
            // to the expected hardware buffer size.
            // Reference: SDL_wasapi.c line ~499
            if (calculatedFrames > (int)bufferFrameCount) {
                calculatedFrames = (int)bufferFrameCount;
            }

            Guid iidRenderClient = SdlWasapiGuids.IidIaudioRenderClient;
            hr = _audioClient.GetService(ref iidRenderClient, out IntPtr renderClientPtr);
            if (SdlWasapiResult.Failed(hr) || renderClientPtr == IntPtr.Zero) {
                error = $"Failed to get render client: 0x{hr:X8}";
                return false;
            }

            IAudioRenderClient renderClient = (IAudioRenderClient)Marshal.GetTypedObjectForIUnknown(
                renderClientPtr,
                typeof(IAudioRenderClient));
            Marshal.Release(renderClientPtr);
            _renderClient = renderClient;
            _sampleFrames = calculatedFrames;
            _bufferFrameCount = (int)bufferFrameCount;
            // _channels was already set above from the device's native mix format
            _bytesPerFrame = _channels * sizeof(float);

            // Reference: SDL_wasapi.c WASAPI_PrepDevice line ~536
            // IAudioClient_Start(client) is called at the end of PrepDevice
            hr = _audioClient.Start();
            if (SdlWasapiResult.Failed(hr)) {
                error = $"Failed to start audio client: 0x{hr:X8}";
                return false;
            }

            obtainedSpec = new AudioSpec {
                SampleRate = desiredSpec.SampleRate,
                Channels = _channels,
                BufferFrames = calculatedFrames,
                Callback = desiredSpec.Callback,
                PostmixCallback = desiredSpec.PostmixCallback
            };

            sampleFrames = calculatedFrames;

            return true;
        } catch (COMException ex) {
            error = $"COM exception during Open: {ex.Message} (0x{ex.HResult:X8})";
            return false;
        }
    }

    public void CloseDevice(SdlAudioDevice device) {
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
    }

    public bool WaitDevice(SdlAudioDevice device) {
        if (_audioClient == null || _renderClient == null || _bufferEvent == IntPtr.Zero) {
            return false;
        }

        // Reference: SDL_wasapi.c WASAPI_WaitDevice
        // Wait for WASAPI buffer event, then check padding to see if we can write.
        // For playback: break when padding <= maxpadding (spec.samples)
        while (true) {
            uint waitResult = NativeMethods.WaitForSingleObjectEx(_bufferEvent, WaitTimeoutMs, false);
            if (waitResult == WaitObject0) {
                int hr = _audioClient.GetCurrentPadding(out uint padding);
                if (SdlWasapiResult.Failed(hr)) {
                    return false;
                }

                // Reference: SDL_wasapi.c line ~285
                // const UINT32 maxpadding = this->spec.samples;
                // if (padding <= maxpadding) { break; }
                if (padding <= (uint)_sampleFrames) {
                    return true;
                }
                // Not enough room yet, keep waiting
            } else if (waitResult == WaitTimeout) {
                continue;
            } else {
                _audioClient.Stop();
                return false;
            }
        }
    }

    public IntPtr GetDeviceBuffer(SdlAudioDevice device, out int bufferBytes) {
        if (_renderClient == null) {
            bufferBytes = -1;
            return IntPtr.Zero;
        }

        // Reference: SDL_wasapi.c WASAPI_GetDeviceBuf lines 183-198
        // SDL retries internally on BUFFER_TOO_LARGE by calling WaitDevice in a loop.
        // This is critical for glitch-free playback - the caller must receive a valid
        // buffer pointer every time, not a "try again" signal.
        while (true) {
            int hr = _renderClient.GetBuffer((uint)_sampleFrames, out IntPtr dataPtr);
            if (hr == SdlWasapiResult.AudioClientEBufferTooLarge) {
                // Not enough room yet - wait for buffer to drain
                // Reference: SDL_wasapi.c line 191: WASAPI_WaitDevice(this)
                if (!WaitDevice(device)) {
                    bufferBytes = -1;
                    return IntPtr.Zero;
                }
                continue; // retry GetBuffer
            }

            if (SdlWasapiResult.Failed(hr)) {
                bufferBytes = -1;
                return IntPtr.Zero;
            }

            bufferBytes = _sampleFrames * _bytesPerFrame;
            return dataPtr;
        }
    }

    public bool PlayDevice(SdlAudioDevice device, IntPtr buffer, int bufferBytes) {
        if (_renderClient == null) {
            return false;
        }

        // Reference: SDL_wasapi.c WASAPI_PlayDevice
        // Releases exactly spec.samples frames (the same count passed to GetBuffer)
        int hr = _renderClient.ReleaseBuffer((uint)_sampleFrames, 0);
        return !SdlWasapiResult.Failed(hr);
    }

    public void ThreadInit(SdlAudioDevice device) {
        // Reference: SDL_wasapi_win32.c WASAPI_PlatformThreadInit
        // this thread uses COM.
        NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_MULTITHREADED);

        // Reference: SDL_wasapi_win32.c WASAPI_PlatformThreadInit
        // Set this thread to very high "Pro Audio" priority.
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
    }

    public void ThreadDeinit(SdlAudioDevice device) {
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

        NativeMethods.CoUninitialize();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate IntPtr AvSetMmThreadCharacteristicsWDelegate(string taskName, ref uint taskIndex);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool AvRevertMmThreadCharacteristicsDelegate(IntPtr taskHandle);

    private static partial class NativeMethods {
        public const uint COINIT_MULTITHREADED = 0x0;

        [LibraryImport("ole32.dll")]
        public static partial int CoInitializeEx(IntPtr reserved, uint coInit);

        [LibraryImport("ole32.dll")]
        public static partial void CoUninitialize();

        [LibraryImport("ole32.dll")]
        public static partial void CoTaskMemFree(IntPtr ptr);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObjectEx(IntPtr hHandle, int dwMilliseconds, bool bAlertable);

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hLibModule);

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
        public static partial IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
    }
}
