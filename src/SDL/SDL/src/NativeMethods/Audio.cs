using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern int SDL_AudioInit(/*const char*/ byte* driver_name);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_AudioQuit();

        [DllImport(LibSDL2Name)]
        public static extern int SDL_BuildAudioCVT(
          out SDL_AudioCVT cvt,
          AudioDataFormat src_format,
          byte src_channels,
          int src_rate,
          AudioDataFormat dst_format,
          byte dst_channels,
          int dst_rate
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_ClearQueuedAudio(uint dev);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_CloseAudioDevice(uint dev);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_ConvertAudio(ref SDL_AudioCVT cvt);

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_DequeueAudio(uint dev, void* data, uint len);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_FreeWAV(byte* buffer);

        [DllImport(LibSDL2Name)]
        public static extern SDL_AudioSpec* SDL_LoadWAV_RW(
          RWOps src,
          int freesrc,
          out SDL_AudioSpec spec,
          out byte* audio_buf,
          out uint audio_len);

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GetAudioDeviceName(int index, int iscapture);

        [DllImport(LibSDL2Name)]
        public static extern AudioStatus SDL_GetAudioDeviceStatus(uint dev);

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GetAudioDriver(int index);

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GetCurrentAudioDriver();

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetNumAudioDevices(int iscapture);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetNumAudioDrivers();

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetQueuedAudioSize(uint dev);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_LockAudioDevice(uint dev);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_MixAudioFormat(
          byte* dst,
          /*const*/ byte* src,
          AudioDataFormat format,
          uint len,
          int volume);

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_OpenAudioDevice(
          /*const char*/ byte* device,
          int iscapture,
          in SDL_AudioSpec desired,
          out SDL_AudioSpec obtained,
          AllowedAudioStreamChange allowed_changes
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_PauseAudioDevice(uint dev, int pause_on);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_UnlockAudioDevice(uint dev);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_QueueAudio(
            uint dev,
            /*const*/ byte* data,
            uint len);

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_AudioCVT
        {
            public int needed;
            public AudioDataFormat src_format, dst_format;
            public double rate_incr;
            public byte* buf;
            public int len, len_cvt, len_mult;
            public double len_ratio;
            SDL_AudioFilter
              filters0, filters1, filters2, filters3,
              filters4, filters5, filters6, filters7,
              filters8, filters9;
            int filter_index;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_AudioSpec
        {
            public int freq;
            public AudioDataFormat format;
            public byte channels, silence;
            public ushort samples;
            ushort padding;
            public uint size;
            public IntPtr callback; // SDL_AudioCallback
            public void* userdata;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SDL_AudioCallback(void* userdata, byte* stream, int len);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SDL_AudioFilter(ref SDL_AudioCVT cvt, AudioDataFormat format);
    }

    public enum AudioStatus
    {
        Stopped,
        Playing,
        Paused,
    }

    public enum AudioDataFormat : ushort
    {
        Unknown = 0,

        Unsigned8Bit = 0x0008,
        Signed8Bit = 0x8008,
        Unsigned16BitLSB = 0x0010,
        Signed16BitLSB = 0x8010,
        Unsigned16BitMSB = 0x1010,
        Signed16BitMSB = 0x9010,
        Unsigned16Bit = Unsigned16BitLSB,
        Signed16Bit = Signed16BitLSB,

        Signed32BitLSB = 0x8020,
        Signed32BitMSB = 0x9020,
        Signed32Bit = Signed32BitLSB,

        Float32BitLSB = 0x8120,
        Float32BitMSB = 0x9120,
        Float32Bit = Float32BitLSB,

        IsFloat = 1 << 8,
        IsBigEndian = 1 << 12,
        IsSigned = 1 << 15,
        SizeMask = 0xFF,
    }

    [Flags]
    public enum AllowedAudioStreamChange
    {
        None = 0,
        Frequency = 1,
        Format = 2,
        Channels = 4,
        Any = Frequency | Format | Channels,
    }
}
