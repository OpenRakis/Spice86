using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    internal static unsafe class MixerNativeMethods
    {
        private const string LibSdl2MixerName = "SDL2_mixer";

        [DllImport(LibSdl2MixerName)]
        public static extern /*const*/ SDL_Version* Mix_Linked_Version();

        [DllImport(LibSdl2MixerName)]
        public static extern MixerLoaders Mix_Init(MixerLoaders loaders);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_Quit();

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_OpenAudio(int frequency, ushort format, int channels, int chunksize);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_OpenAudio(int frequency, ushort format, int channels, int chunksize, /*const char*/ byte* device, int allowed_changes);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_AllocateChannels(int numchans);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_QuerySpec(IntPtr frequency, out ushort format, IntPtr channels);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_QuerySpec(IntPtr frequency, IntPtr format, out int channels);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_QuerySpec(out int frequency, IntPtr format, IntPtr channels);

        [DllImport(LibSdl2MixerName)]
        public static extern MixerChunk Mix_LoadWAV_RW(RWOps src, int freesrc);

        [DllImport(LibSdl2MixerName)]
        public static extern MusicTrack Mix_LoadMUS(/*const char*/ byte* file);

        [DllImport(LibSdl2MixerName)]
        public static extern MusicTrack Mix_LoadMUS_RW(RWOps src, int freesrc);

        [DllImport(LibSdl2MixerName)]
        public static extern MusicTrack Mix_LoadMUSType_RW(RWOps src, MusicType type, int freesrc);

        [DllImport(LibSdl2MixerName)]
        public static extern MusicTrack Mix_QuickLoad_WAV(byte* mem);

        [DllImport(LibSdl2MixerName)]
        public static extern MusicTrack Mix_QuickLoad_RAW(byte* mem, uint len);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_FreeChunk(IntPtr chunk);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_FreeMusic(IntPtr music);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_GetNumChunkDecoders();

        [DllImport(LibSdl2MixerName)]
        public static extern /*const char*/ byte* Mix_GetChunkDecoder(int index);

        [DllImport(LibSdl2MixerName)]
        public static extern SDL_Bool Mix_HasChunkDecoder(/*const char*/ byte* name);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_GetNumMusicDecoders();

        [DllImport(LibSdl2MixerName)]
        public static extern /*const char*/ byte* Mix_GetMusicDecoder(int index);

        [DllImport(LibSdl2MixerName)]
        public static extern SDL_Bool Mix_HasMusicDecoder(/*const char*/ byte* name);

        [DllImport(LibSdl2MixerName)]
        public static extern MusicType Mix_GetMusicType(MusicTrack music);

        [DllImport(LibSdl2MixerName)]
        public static extern MusicType Mix_GetMusicType(/*const Mix_Music*/ IntPtr music);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_SetPostMix(IntPtr mix_func, IntPtr arg);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_HookMusic(IntPtr mix_func, IntPtr arg);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_HookMusicFinished(IntPtr music_finished);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_ChannelFinished(IntPtr channel_finished);

        [DllImport(LibSdl2MixerName)]
        public static extern IntPtr Mix_GetMusicHookData();

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_RegisterEffect(int chan, /* Mix_EffectFunc_t */ IntPtr f, /* Mix_EffectDone_t */ IntPtr d, IntPtr arg);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_UnregisterEffect(int chan, /* Mix_EffectFunc_t */ IntPtr f);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_UnregisterAllEffects(int channel);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_SetPanning(int channel, byte left, byte right);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_SetPosition(int channel, short angle, byte distance);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_SetDistance(int channel, byte distance);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_SetReverseStereo(int channel, int flip);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_ReserveChannels(int num);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_GroupChannel(int which, int tag);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_GroupChannels(int from, int to, int tag);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_GroupAvailable(int tag);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_GroupCount(int tag);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_GroupOldest(int tag);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_GroupNewer(int tag);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_PlayChannelTimed(int channel, MixerChunk chunk, int loops, int ticks);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_PlayMusic(MusicTrack music, int loops);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_FadeInMusic(MusicTrack music, int loops, int ms);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_FadeInMusicPos(MusicTrack music, int loops, int ms, double position);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_FadeInChannelTimed(int channel, MixerChunk chunk, int loops, int ms, int ticks);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_Volume(int channel, int volume);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_VolumeChunk(MixerChunk chunk, int volume);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_VolumeMusic(int volume);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_HaltChannel(int channel);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_HaltGroup(int tag);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_HaltMusic();

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_ExpireChannel(int channel, int ticks);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_FadeOutChannel(int which, int ms);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_FadeOutGroup(int tag, int ms);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_FadeOutMusic(int ms);

        [DllImport(LibSdl2MixerName)]
        public static extern Fading Mix_FadingMusic();

        [DllImport(LibSdl2MixerName)]
        public static extern Fading Mix_FadingChannel(int which);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_Pause(int channel);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_Resume(int channel);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_Paused(int channel);

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_PauseMusic();

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_ResumeMusic();

        [DllImport(LibSdl2MixerName)]
        public static extern void Mix_RewindMusic();

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_PausedMusic();

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_SetMusicPosition(double position);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_Playing(int channel);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_PlayingMusic();

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_SetMusicCMD(/*const char*/ byte* command);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_SetSynchroValue(int value);

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_GetSynchroValue();

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_SetSoundFonts(/*const char*/ byte* paths);

        [DllImport(LibSdl2MixerName)]
        public static extern /*const char*/ byte* Mix_GetSoundFonts();

        [DllImport(LibSdl2MixerName)]
        public static extern int Mix_EachSoundFont(IntPtr function, IntPtr data);

        [DllImport(LibSdl2MixerName)]
        public static extern IntPtr Mix_GetChunk(int channel);

        [DllImport(LibSdl2MixerName)]
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
