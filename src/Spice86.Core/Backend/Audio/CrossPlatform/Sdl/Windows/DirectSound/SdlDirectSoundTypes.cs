namespace Spice86.Core.Backend.Audio.CrossPlatform.Sdl.Windows.DirectSound;

using System;
using System.Runtime.InteropServices;

internal static class SdlDirectSoundConstants {
    public const int DsOk = 0;
    public const int DsErrBufferLost = unchecked((int)0x88780096);

    public const uint DsbcapsGetCurrentPosition2 = 0x00010000;
    public const uint DsbcapsGlobalFocus = 0x00008000;

    public const uint DsbstatusPlaying = 0x00000001;
    public const uint DsbstatusBufferLost = 0x00000002;

    public const uint DsbplayLooping = 0x00000001;
    public const uint DsblockEntireBuffer = 0x00000002;

    public const uint DssclNormal = 0x00000001;
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

[StructLayout(LayoutKind.Sequential)]
internal struct DsBufferDesc {
    public uint Size;
    public uint Flags;
    public uint BufferBytes;
    public uint Reserved;
    public IntPtr Format;
    public Guid Guid3DAlgorithm;
}
