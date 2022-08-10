using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.MixerNativeMethods;

namespace SDLSharp
{
    public class MixerChannels : IReadOnlyList<MixerChannel>
    {
        ChannelFinished? cb;

        internal MixerChannels()
        {
            cb = null;
        }

        private event EventHandler<MixerChannelFinishedPlaybackEventArgs>? channelFinished;

        public event EventHandler<MixerChannelFinishedPlaybackEventArgs> ChannelFinished
        {
            add
            {
                if (cb == null)
                {
                    cb = (int channel) =>
                    {
                        MixerChannel? chan = this[channel];
                        channelFinished?.Invoke(chan, new MixerChannelFinishedPlaybackEventArgs(chan));
                    };
                    Mix_ChannelFinished(Marshal.GetFunctionPointerForDelegate(cb));
                }
                channelFinished += value;
            }
            remove
            {
                channelFinished -= value;
            }
        }

        public int Count
        {
            get
            {
                return Mix_AllocateChannels(-1);
            }
            set
            {
                Mix_AllocateChannels(value);
            }
        }

        public int Reserved
        {
            get
            {
                return Mix_ReserveChannels(-1);
            }
            set
            {
                Mix_ReserveChannels(value);
            }
        }

        public int Playing => Mix_Playing(-1);
        public int Paused => Mix_Paused(-1);


        public unsafe MixerChannel this[int index]
        {
            get
            {
                if (index >= Count)
                    throw new IndexOutOfRangeException();
                return new MixerChannel(index);
            }
        }

        public int Volume
        {
            get
            {
                return Mix_Volume(-1, -1);
            }
            set
            {
                Mix_Volume(-1, value);
            }
        }

        public bool Play(MixerChunk chunk, int maxLoops = 1, int maxMilliseconds = -1)
        {
            return PlayingResult(Mix_PlayChannelTimed(-1, chunk, maxLoops - 1, maxMilliseconds));
        }

        public bool FadeIn(MixerChunk chunk, int milliseconds, int maxLoops = 1, int maxMilliseconds = -1)
        {
            return PlayingResult(Mix_FadeInChannelTimed(-1, chunk, maxLoops - 1, milliseconds, maxMilliseconds));
        }

        public void Pause()
        {
            Mix_Pause(-1);
        }

        public void Resume()
        {
            Mix_Pause(-1);
        }

        public void Halt()
        {
            Mix_HaltChannel(-1);
        }

        public void Expire(int inMilliseconds)
        {
            Mix_ExpireChannel(-1, inMilliseconds);
        }

        public void FadeOut(int inMilliseconds)
        {
            Mix_FadeOutChannel(-1, inMilliseconds);
        }

        IEnumerable<MixerChannel> Enumerate()
        {
            int c = Count;
            for (int i = 0; i < c; ++i)
                yield return this[i];
        }

        public IEnumerator<MixerChannel> GetEnumerator()
        {
            return this.Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }

    public class MixerChannelFinishedPlaybackEventArgs : EventArgs
    {
        public MixerChannel Channel { get; }
        public MixerChannelFinishedPlaybackEventArgs(MixerChannel channel)
        {
            this.Channel = channel;
        }
    }
}
