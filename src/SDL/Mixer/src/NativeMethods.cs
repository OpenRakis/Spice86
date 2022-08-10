using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    internal static unsafe class MixerNativeMethods
    {

        [DllImport("SDL2_mixer")]
        public static extern /*const*/ SDL_Version* Mix_Linked_Version();

        [DllImport("SDL2_mixer")]
        public static extern MixerLoaders Mix_Init(MixerLoaders loaders);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_Quit();

        [DllImport("SDL2_mixer")]
        public static extern int Mix_OpenAudio(int frequency, ushort format, int channels, int chunksize);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_OpenAudio(int frequency, ushort format, int channels, int chunksize, /*const char*/ byte* device, int allowed_changes);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_AllocateChannels(int numchans);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_QuerySpec(IntPtr frequency, out ushort format, IntPtr channels);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_QuerySpec(IntPtr frequency, IntPtr format, out int channels);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_QuerySpec(out int frequency, IntPtr format, IntPtr channels);

        [DllImport("SDL2_mixer")]
        public static extern MixerChunk Mix_LoadWAV_RW(RWOps src, int freesrc);

        [DllImport("SDL2_mixer")]
        public static extern MusicTrack Mix_LoadMUS(/*const char*/ byte* file);

        [DllImport("SDL2_mixer")]
        public static extern MusicTrack Mix_LoadMUS_RW(RWOps src, int freesrc);

        [DllImport("SDL2_mixer")]
        public static extern MusicTrack Mix_LoadMUSType_RW(RWOps src, MusicType type, int freesrc);

        [DllImport("SDL2_mixer")]
        public static extern MusicTrack Mix_QuickLoad_WAV(byte* mem);

        [DllImport("SDL2_mixer")]
        public static extern MusicTrack Mix_QuickLoad_RAW(byte* mem, uint len);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_FreeChunk(IntPtr chunk);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_FreeMusic(IntPtr music);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_GetNumChunkDecoders();

        [DllImport("SDL2_mixer")]
        public static extern /*const char*/ byte* Mix_GetChunkDecoder(int index);

        [DllImport("SDL2_mixer")]
        public static extern SDL_Bool Mix_HasChunkDecoder(/*const char*/ byte* name);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_GetNumMusicDecoders();

        [DllImport("SDL2_mixer")]
        public static extern /*const char*/ byte* Mix_GetMusicDecoder(int index);

        [DllImport("SDL2_mixer")]
        public static extern SDL_Bool Mix_HasMusicDecoder(/*const char*/ byte* name);

        [DllImport("SDL2_mixer")]
        public static extern MusicType Mix_GetMusicType(MusicTrack music);

        [DllImport("SDL2_mixer")]
        public static extern MusicType Mix_GetMusicType(/*const Mix_Music*/ IntPtr music);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_SetPostMix(IntPtr mix_func, IntPtr arg);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_HookMusic(IntPtr mix_func, IntPtr arg);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_HookMusicFinished(IntPtr music_finished);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_ChannelFinished(IntPtr channel_finished);

        [DllImport("SDL2_mixer")]
        public static extern IntPtr Mix_GetMusicHookData();

        [DllImport("SDL2_mixer")]
        public static extern int Mix_RegisterEffect(int chan, /* Mix_EffectFunc_t */ IntPtr f, /* Mix_EffectDone_t */ IntPtr d, IntPtr arg);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_UnregisterEffect(int chan, /* Mix_EffectFunc_t */ IntPtr f);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_UnregisterAllEffects(int channel);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_SetPanning(int channel, byte left, byte right);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_SetPosition(int channel, short angle, byte distance);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_SetDistance(int channel, byte distance);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_SetReverseStereo(int channel, int flip);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_ReserveChannels(int num);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_GroupChannel(int which, int tag);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_GroupChannels(int from, int to, int tag);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_GroupAvailable(int tag);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_GroupCount(int tag);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_GroupOldest(int tag);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_GroupNewer(int tag);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_PlayChannelTimed(int channel, MixerChunk chunk, int loops, int ticks);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_PlayMusic(MusicTrack music, int loops);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_FadeInMusic(MusicTrack music, int loops, int ms);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_FadeInMusicPos(MusicTrack music, int loops, int ms, double position);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_FadeInChannelTimed(int channel, MixerChunk chunk, int loops, int ms, int ticks);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_Volume(int channel, int volume);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_VolumeChunk(MixerChunk chunk, int volume);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_VolumeMusic(int volume);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_HaltChannel(int channel);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_HaltGroup(int tag);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_HaltMusic();

        [DllImport("SDL2_mixer")]
        public static extern int Mix_ExpireChannel(int channel, int ticks);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_FadeOutChannel(int which, int ms);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_FadeOutGroup(int tag, int ms);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_FadeOutMusic(int ms);

        [DllImport("SDL2_mixer")]
        public static extern Fading Mix_FadingMusic();

        [DllImport("SDL2_mixer")]
        public static extern Fading Mix_FadingChannel(int which);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_Pause(int channel);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_Resume(int channel);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_Paused(int channel);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_PauseMusic();

        [DllImport("SDL2_mixer")]
        public static extern void Mix_ResumeMusic();

        [DllImport("SDL2_mixer")]
        public static extern void Mix_RewindMusic();

        [DllImport("SDL2_mixer")]
        public static extern int Mix_PausedMusic();

        [DllImport("SDL2_mixer")]
        public static extern int Mix_SetMusicPosition(double position);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_Playing(int channel);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_PlayingMusic();

        [DllImport("SDL2_mixer")]
        public static extern int Mix_SetMusicCMD(/*const char*/ byte* command);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_SetSynchroValue(int value);

        [DllImport("SDL2_mixer")]
        public static extern int Mix_GetSynchroValue();

        [DllImport("SDL2_mixer")]
        public static extern int Mix_SetSoundFonts(/*const char*/ byte* paths);

        [DllImport("SDL2_mixer")]
        public static extern /*const char*/ byte* Mix_GetSoundFonts();

        [DllImport("SDL2_mixer")]
        public static extern int Mix_EachSoundFont(IntPtr function, IntPtr data);

        [DllImport("SDL2_mixer")]
        public static extern IntPtr Mix_GetChunk(int channel);

        [DllImport("SDL2_mixer")]
        public static extern void Mix_CloseAudio();


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void MusicPlayerFunc(IntPtr udata, byte* stream, int len);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void MusicFinished();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ChannelFinished(int channel);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void Mix_EffectFunc_t(int chan, byte* stream, int len, IntPtr udata);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void Mix_EffectDone_t(int chan, IntPtr udata);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void EachSoundFont(/*const char*/ byte* a, IntPtr udata);

        [StructLayout(LayoutKind.Sequential)]
        public struct Mix_Chunk
        {
            public int allocated;
            public byte* abuf;
            public uint alen;
            public byte volume;
        }

        public static bool PlayingResult(int res)
        {
            if (res == -1)
            {
                SDLException? err = GetError();
                if (err != null && err.Message != "No free channels available")
                    throw err;
                return false;
            }
            return true;
        }

    }

    [Flags]
    public enum MixerLoaders
    {
        FLAC = 1,
        MOD = 2,
        MP3 = 8,
        OGG = 8,
        MID = 8,
        Opus = 8,
    }

    public enum Fading
    {
        None,
        Out,
        In,
    }

    public enum MusicType
    {
        None,
        Cmd,
        Wave,
        MOD,
        MID,
        OGG,
        MP3,
        FLAC,
        Opus,
    }
}
