using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.MixerNativeMethods;

namespace SDLSharp
{
    public static class Mixer
    {
        public static MixerChannels Channels { get; } = new MixerChannels();
        public static Music Music { get; } = new Music();

        public static MixerLoaders Init(MixerLoaders loaders)
        {
            MixerLoaders got = Mix_Init(loaders);
            if ((got & loaders) != loaders)
            {
                SDLException? err = GetError();
                if (err != null)
                    throw err;
            }
            return got;
        }

        public static void Quit()
        {
            Mix_Quit();
        }

        public static unsafe Version RuntimeVersion
        {
            get
            {
                SDL_Version* v = Mix_Linked_Version();
                return new Version(v->major, v->minor, v->patch, 0);
            }
        }

        public static MixerLoaders InitializedLoaders => Mix_Init((MixerLoaders)0);

        public static void Open(int frequency, AudioDataFormat format, int channels, int chunksize)
        {
            ErrorIfNegative(Mix_OpenAudio(frequency, (ushort)format, channels, chunksize));
        }

        public static void Close()
        {
            Mix_CloseAudio();
        }

        public static int DeviceFrequency
        {
            get
            {
                int freq;
                ErrorIfZero(Mix_QuerySpec(out freq, IntPtr.Zero, IntPtr.Zero));
                return freq;
            }
        }

        public static ushort DeviceFormat
        {
            get
            {
                ushort fmt;
                ErrorIfZero(Mix_QuerySpec(IntPtr.Zero, out fmt, IntPtr.Zero));
                return fmt;
            }
        }

        public static int DeviceChannels
        {
            get
            {
                int channels;
                ErrorIfZero(Mix_QuerySpec(IntPtr.Zero, IntPtr.Zero, out channels));
                return channels;
            }
        }
    }
}
