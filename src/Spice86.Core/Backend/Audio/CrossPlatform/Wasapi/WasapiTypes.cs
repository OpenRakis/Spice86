namespace Spice86.Core.Backend.Audio.CrossPlatform.Wasapi;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// WASAPI audio format tag values.
/// </summary>
internal static class WaveFormatTag {
    public const ushort Pcm = 1;
    public const ushort IeeeFloat = 3;
    public const ushort Extensible = 0xFFFE;
}

/// <summary>
/// WAVEFORMATEX structure for basic audio formats.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatEx {
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort CbSize;

    public static WaveFormatEx CreateIeeeFloat(int sampleRate, int channels) {
        ushort bitsPerSample = 32;
        ushort blockAlign = (ushort)(channels * bitsPerSample / 8);
        return new WaveFormatEx {
            FormatTag = WaveFormatTag.IeeeFloat,
            Channels = (ushort)channels,
            SamplesPerSec = (uint)sampleRate,
            AvgBytesPerSec = (uint)(sampleRate * blockAlign),
            BlockAlign = blockAlign,
            BitsPerSample = bitsPerSample,
            CbSize = 0
        };
    }
}

/// <summary>
/// WAVEFORMATEXTENSIBLE structure for extensible audio formats.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatExtensible {
    public WaveFormatEx Format;
    public ushort ValidBitsPerSample;
    public uint ChannelMask;
    public Guid SubFormat;

    public static readonly Guid KsdataformatSubtypeIeeeFloat = new Guid("00000003-0000-0010-8000-00aa00389b71");

    public static WaveFormatExtensible CreateIeeeFloat(int sampleRate, int channels) {
        WaveFormatEx format = WaveFormatEx.CreateIeeeFloat(sampleRate, channels);
        format.FormatTag = WaveFormatTag.Extensible;
        format.CbSize = 22;
        return new WaveFormatExtensible {
            Format = format,
            ValidBitsPerSample = 32,
            ChannelMask = (uint)(channels == 2 ? 0x3 : 0x4), // SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT or SPEAKER_FRONT_CENTER
            SubFormat = KsdataformatSubtypeIeeeFloat
        };
    }
}

/// <summary>
/// AUDCLNT_SHAREMODE enumeration.
/// </summary>
internal enum AudioClientShareMode {
    Shared = 0,
    Exclusive = 1
}

/// <summary>
/// AUDCLNT_STREAMFLAGS enumeration.
/// </summary>
[Flags]
internal enum AudioClientStreamFlags : uint {
    None = 0,
    CrossProcess = 0x00010000,
    Loopback = 0x00020000,
    EventCallback = 0x00040000,
    NoPersist = 0x00080000,
    RateAdjust = 0x00100000,
    AutoConvertPcm = 0x80000000,
    SrcDefaultQuality = 0x08000000
}

/// <summary>
/// AUDIO_STREAM_CATEGORY enumeration.
/// </summary>
internal enum AudioStreamCategory {
    Other = 0,
    ForegroundOnlyMedia = 1,
    BackgroundCapableMedia = 2,
    Communications = 3,
    Alerts = 4,
    SoundEffects = 5,
    GameEffects = 6,
    GameMedia = 7,
    GameChat = 8,
    Speech = 9,
    Movie = 10,
    Media = 11
}

/// <summary>
/// EDataFlow enumeration for device type.
/// </summary>
internal enum DataFlow {
    Render = 0,
    Capture = 1,
    All = 2
}

/// <summary>
/// ERole enumeration for device role.
/// </summary>
internal enum Role {
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

/// <summary>
/// WASAPI HRESULT codes.
/// </summary>
internal static class WasapiResult {
    public const int SOk = 0;
    public const int SFalse = 1;
    public const int ENotImpl = unchecked((int)0x80004001);
    public const int ENoInterface = unchecked((int)0x80004002);
    public const int EPointer = unchecked((int)0x80004003);
    public const int EAbort = unchecked((int)0x80004004);
    public const int EFail = unchecked((int)0x80004005);
    public const int EInvalidArg = unchecked((int)0x80070057);
    public const int AudioClientENotInitialized = unchecked((int)0x88890001);
    public const int AudioClientEAlreadyInitialized = unchecked((int)0x88890002);
    public const int AudioClientEWrongEndpointType = unchecked((int)0x88890003);
    public const int AudioClientEDeviceInvalidated = unchecked((int)0x88890004);
    public const int AudioClientENotStopped = unchecked((int)0x88890005);
    public const int AudioClientEBufferTooLarge = unchecked((int)0x88890006);
    public const int AudioClientEOutOfOrder = unchecked((int)0x88890007);
    public const int AudioClientEUnsupportedFormat = unchecked((int)0x88890008);
    public const int AudioClientEInvalidSize = unchecked((int)0x88890009);
    public const int AudioClientEDeviceInUse = unchecked((int)0x8889000A);
    public const int AudioClientEBufferOperationPending = unchecked((int)0x8889000B);
    public const int AudioClientEThreadNotRegistered = unchecked((int)0x8889000C);
    public const int AudioClientEExclusiveModeNotAllowed = unchecked((int)0x8889000E);
    public const int AudioClientEEndpointCreateFailed = unchecked((int)0x8889000F);
    public const int AudioClientEServiceNotRunning = unchecked((int)0x88890010);
    public const int AudioClientEEventHandleNotExpected = unchecked((int)0x88890011);
    public const int AudioClientEExclusiveModeOnly = unchecked((int)0x88890012);
    public const int AudioClientEBufdurationPeriodNotEqual = unchecked((int)0x88890013);
    public const int AudioClientEEventHandleNotSet = unchecked((int)0x88890014);
    public const int AudioClientEIncorrectBufferSize = unchecked((int)0x88890015);
    public const int AudioClientEBufferSizeError = unchecked((int)0x88890016);
    public const int AudioClientECpuUsageExceeded = unchecked((int)0x88890017);
    public const int AudioClientEBufferError = unchecked((int)0x88890018);
    public const int AudioClientEBufferSizeNotAligned = unchecked((int)0x88890019);
    public const int AudioClientEInvalidDevicePeriod = unchecked((int)0x88890020);

    public static bool Succeeded(int hr) => hr >= 0;
    public static bool Failed(int hr) => hr < 0;
}

/// <summary>
/// COM CLSID for MMDeviceEnumerator.
/// </summary>
internal static class WasapiGuids {
    public static readonly Guid ClsidMmDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IidImmDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
    public static readonly Guid IidImmDevice = new Guid("D666063F-1587-4E43-81F1-B948E807363F");
    public static readonly Guid IidIaudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid IidIaudioRenderClient = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
}
