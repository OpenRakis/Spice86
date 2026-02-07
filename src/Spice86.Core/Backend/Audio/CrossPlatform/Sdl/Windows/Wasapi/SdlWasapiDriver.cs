namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows.Wasapi;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

[SupportedOSPlatform("windows")]
internal sealed class SdlWasapiDriver : ISdlAudioDriver {
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

            _bufferEvent = NativeMethods.CreateEventW(IntPtr.Zero, false, false, null);
            if (_bufferEvent == IntPtr.Zero) {
                error = "Failed to create event handle";
                return false;
            }

            IntPtr mixFormatPtr = IntPtr.Zero;
            hr = _audioClient.GetMixFormat(out mixFormatPtr);
            if (SdlWasapiResult.Failed(hr) || mixFormatPtr == IntPtr.Zero) {
                error = $"Failed to get mix format: 0x{hr:X8}";
                return false;
            }

            WaveFormatEx format = WaveFormatEx.CreateIeeeFloat(desiredSpec.SampleRate, desiredSpec.Channels);
            IntPtr formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
            try {
                Marshal.StructureToPtr(format, formatPtr, false);

                AudioClientStreamFlags flags = AudioClientStreamFlags.EventCallback |
                                              AudioClientStreamFlags.AutoConvertPcm |
                                              AudioClientStreamFlags.SrcDefaultQuality;

                hr = _audioClient.Initialize(AudioClientShareMode.Shared, flags, 0, 0, formatPtr, IntPtr.Zero);
                if (SdlWasapiResult.Failed(hr)) {
                    error = $"Failed to initialize audio client: 0x{hr:X8}";
                    return false;
                }
            } finally {
                Marshal.FreeHGlobal(formatPtr);
                NativeMethods.CoTaskMemFree(mixFormatPtr);
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

            hr = _audioClient.GetDevicePeriod(out long defaultPeriod, out _);
            if (SdlWasapiResult.Failed(hr)) {
                error = $"Failed to get device period: 0x{hr:X8}";
                return false;
            }

            float periodMillis = defaultPeriod / 10000.0f;
            float periodFrames = periodMillis * desiredSpec.SampleRate / 1000.0f;
            int calculatedFrames = (int)MathF.Ceiling(periodFrames);

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
            _channels = desiredSpec.Channels;
            _bytesPerFrame = _channels * sizeof(float);

            obtainedSpec = new AudioSpec {
                SampleRate = desiredSpec.SampleRate,
                Channels = desiredSpec.Channels,
                BufferFrames = calculatedFrames,
                Callback = desiredSpec.Callback,
                PostmixCallback = desiredSpec.PostmixCallback
            };

            sampleFrames = calculatedFrames;

            hr = _audioClient.Start();
            if (SdlWasapiResult.Failed(hr)) {
                error = $"Failed to start audio client: 0x{hr:X8}";
                return false;
            }

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

        while (true) {
            uint waitResult = NativeMethods.WaitForSingleObjectEx(_bufferEvent, WaitTimeoutMs, false);
            if (waitResult == WaitObject0) {
                int hr = _audioClient.GetCurrentPadding(out uint padding);
                if (SdlWasapiResult.Failed(hr)) {
                    return false;
                }

                if (_sampleFrames <= _bufferFrameCount && padding <= (uint)_sampleFrames) {
                    return true;
                }
            } else if (waitResult == WaitTimeout) {
                continue;
            } else {
                _audioClient.Stop();
                return false;
            }
        }
    }

    public IntPtr GetDeviceBuffer(SdlAudioDevice device, out int bufferBytes) {
        bufferBytes = 0;
        if (_renderClient == null) {
            bufferBytes = -1;
            return IntPtr.Zero;
        }

        int hr = _renderClient.GetBuffer((uint)_sampleFrames, out IntPtr dataPtr);
        if (hr == SdlWasapiResult.AudioClientEBufferTooLarge) {
            bufferBytes = 0;
            return IntPtr.Zero;
        }

        if (SdlWasapiResult.Failed(hr)) {
            bufferBytes = -1;
            return IntPtr.Zero;
        }

        bufferBytes = _sampleFrames * _bytesPerFrame;
        return dataPtr;
    }

    public bool PlayDevice(SdlAudioDevice device, IntPtr buffer, int bufferBytes) {
        if (_renderClient == null) {
            return false;
        }

        _renderClient.ReleaseBuffer((uint)_sampleFrames, 0);
        return true;
    }

    public void ThreadInit(SdlAudioDevice device) {
        NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_MULTITHREADED);

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
