using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.MixerNativeMethods;

namespace SDLSharp
{
    public class MixerChannelGroup
    {
        readonly int tag;

        public MixerChannelGroup(int tag)
        {
            this.tag = tag;
        }

        public int Tag => tag;
        public int Count => Mix_GroupCount(tag);

        public void Add(MixerChannel c)
        {
            Mix_GroupChannel(c.Number, tag);
        }

        public bool Add(int channel)
        {
            return Mix_GroupChannel(channel, tag) != 0;
        }

        public bool Add(int fromChannel, int toChannel)
        {
            return Mix_GroupChannels(fromChannel, toChannel, tag) != 0;
        }

        public MixerChannel? FirstAvailable()
        {
            int c = Mix_GroupAvailable(tag);
            return c < 0 ? null : new MixerChannel(c);
        }

        public MixerChannel? OldestPlaying()
        {
            int c = Mix_GroupOldest(tag);
            return c < 0 ? null : new MixerChannel(c);
        }

        public MixerChannel? NewestPlaying()
        {
            int c = Mix_GroupOldest(tag);
            return c < 0 ? null : new MixerChannel(c);
        }

        public int FadeOut(int inMilliseconds)
        {
            return Mix_FadeOutGroup(tag, inMilliseconds);
        }

        public int Halt()
        {
            return Mix_HaltGroup(tag);
        }

        public override string ToString()
        {
            return $"ChannelGroup{tag}";
        }
    }
}
