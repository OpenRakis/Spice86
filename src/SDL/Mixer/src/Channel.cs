using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.MixerNativeMethods;

namespace SDLSharp
{
    public class MixerChannel
    {
        readonly int num;

        internal MixerChannel(int num)
        {
            if (num < 0) throw new ArgumentException();
            this.num = num;
        }

        public int Number => num;

        public int Volume
        {
            get
            {
                return Mix_Volume(num, -1);
            }
            set
            {
                Mix_Volume(num, value);
            }
        }

        public bool Playing => Mix_Playing(num) == 1;
        public bool Paused => Mix_Paused(num) == 1;
        public Fading Fading => Mix_FadingChannel(num);

        public bool IsPlaying(MixerChunk chunk)
        {
            IntPtr p = Mix_GetChunk(num);
            return p == chunk.DangerousGetHandle();
        }


        public bool Play(MixerChunk chunk, int maxLoops = 1, int maxMilliseconds = -1)
        {
            return PlayingResult(Mix_PlayChannelTimed(num, chunk, maxLoops - 1, maxMilliseconds));
        }

        public bool FadeIn(MixerChunk chunk, int milliseconds, int maxLoops = 1, int maxMilliseconds = -1)
        {
            return PlayingResult(Mix_FadeInChannelTimed(num, chunk, maxLoops - 1, milliseconds, maxMilliseconds));
        }

        public void Pause()
        {
            Mix_Pause(num);
        }

        public void Resume()
        {
            Mix_Pause(num);
        }

        public void Halt()
        {
            Mix_HaltChannel(num);
        }

        public void Expire(int inMilliseconds)
        {
            Mix_ExpireChannel(num, inMilliseconds);
        }

        public void FadeOut(int inMilliseconds)
        {
            Mix_FadeOutChannel(num, inMilliseconds);
        }

        public override string ToString()
        {
            return $"Channel{num}";
        }
    }
}
