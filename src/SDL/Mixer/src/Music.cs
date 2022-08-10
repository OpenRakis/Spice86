using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.MixerNativeMethods;

namespace SDLSharp
{
    public class Music
    {
        internal Music() { }

        public int Volume
        {
            get
            {
                return Mix_VolumeMusic(-1);
            }
            set
            {
                Mix_VolumeMusic(value);
            }
        }

        public MusicType Type => Mix_GetMusicType(IntPtr.Zero);
        public bool Playing => Mix_PlayingMusic() != 0;
        public bool Paused => Mix_PausedMusic() != 0;
        public Fading Fading => Mix_FadingMusic();


        public bool Play(MusicTrack track, int loops = -1)
        {
            return PlayingResult(Mix_PlayMusic(track, loops));
        }

        public bool FadeIn(MusicTrack track, int milliseconds, int loops = -1)
        {
            return PlayingResult(Mix_FadeInMusic(track, loops, milliseconds));
        }

        public bool FadeIn(MusicTrack track, int milliseconds, double position, int loops = -1)
        {
            return PlayingResult(Mix_FadeInMusicPos(track, loops, milliseconds, position));
        }

        public void Pause()
        {
            Mix_PauseMusic();
        }

        public void Resume()
        {
            Mix_ResumeMusic();
        }

        public void Rewind()
        {
            Mix_RewindMusic();
        }

        public void Halt()
        {
            Mix_HaltMusic();
        }

        public void FadeOut(int inMilliseconds)
        {
            ErrorIfZero(Mix_FadeOutMusic(inMilliseconds));
        }

        public void SetPosition(double position)
        {
            ErrorIfNegative(Mix_SetMusicPosition(position));
        }

        public unsafe void SetCommand(string musicCommand)
        {
            Span<byte> b = stackalloc byte[SL(musicCommand)];
            StringToUTF8(musicCommand, b);
            fixed (byte* p = b)
                ErrorIfNegative(Mix_SetMusicCMD(p));
        }


        MusicPlayerFunc? mfp;
        public unsafe void SetPlayer(CustomPlayer player)
        {
            MusicPlayerFunc mp = (IntPtr ud, byte* stream, int length) =>
            {
                var sp = new Span<byte>(stream, length);
                try
                {
                    player(sp);
                }
                catch (Exception e)
                {
                    SDL.OnUnhandledException(e, true);
                }
            };
            Mix_HookMusic(Marshal.GetFunctionPointerForDelegate(mp), IntPtr.Zero);
            mfp = mp;
        }


        MusicFinished? cb;
        private event EventHandler? musicFinished;

        public event EventHandler Finished
        {
            add
            {
                if (cb == null)
                {
                    cb = () =>
                    {
                        try
                        {
                            musicFinished?.Invoke(EventArgs.Empty, EventArgs.Empty);
                        }
                        catch (Exception e)
                        {
                            SDL.OnUnhandledException(e, true);
                        }
                    };
                    Mix_HookMusicFinished(Marshal.GetFunctionPointerForDelegate(cb));
                }
                musicFinished += value;
            }
            remove
            {
                musicFinished -= value;
            }
        }
    }

    public delegate void CustomPlayer(Span<byte> buffer);
}
