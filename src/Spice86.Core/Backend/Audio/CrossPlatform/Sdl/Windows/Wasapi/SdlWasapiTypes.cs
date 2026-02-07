namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows.Wasapi;

using System;
using System.Runtime.InteropServices;

internal static class SdlWasapiGuids {
    public static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IidIaudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid IidIaudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
}

internal static class SdlWasapiResult {
    public const int AudioClientEBufferTooLarge = unchecked((int)0x88890018);

    public static bool Failed(int hresult) {
        return hresult < 0;
    }
}

internal enum AudioClientShareMode {
    Shared = 0,
    Exclusive = 1
}

[Flags]
internal enum AudioClientStreamFlags {
    None = 0,
    EventCallback = 0x00040000,
    AutoConvertPcm = unchecked((int)0x80000000),
    SrcDefaultQuality = 0x08000000
}

[StructLayout(LayoutKind.Sequential)]
internal struct WaveFormatEx {
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort Size;

    public static WaveFormatEx CreateIeeeFloat(int sampleRate, int channels) {
        WaveFormatEx format = new WaveFormatEx {
            FormatTag = 0x0003,
            Channels = (ushort)channels,
            SamplesPerSec = (uint)sampleRate,
            BitsPerSample = 32
        };
        format.BlockAlign = (ushort)(format.Channels * (format.BitsPerSample / 8));
        format.AvgBytesPerSec = format.SamplesPerSec * format.BlockAlign;
        format.Size = 0;
        return format;
    }
}
