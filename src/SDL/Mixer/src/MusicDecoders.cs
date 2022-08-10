using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.MixerNativeMethods;

namespace SDLSharp
{
    public class MusicDecoders : IReadOnlyList<string>
    {
        internal MusicDecoders() { }

        public int Count => Mix_GetNumMusicDecoders();

        public unsafe string this[int index]
        {
            get
            {
                return UTF8ToString(Mix_GetMusicDecoder(index)) ?? "";
            }
        }

        IEnumerable<string> Enumerate()
        {
            int c = Count;
            for (int i = 0; i < c; ++i)
                yield return this[i];
        }

        public IEnumerator<string> GetEnumerator()
        {
            return this.Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
