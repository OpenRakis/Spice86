using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.MixerNativeMethods;

namespace SDLSharp
{
    public class MixerChunkDecoders : IReadOnlyList<string>
    {
        internal MixerChunkDecoders() { }

        public int Count => Mix_GetNumChunkDecoders();

        public unsafe string this[int index]
        {
            get
            {
                return UTF8ToString(Mix_GetChunkDecoder(index)) ?? "";
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
