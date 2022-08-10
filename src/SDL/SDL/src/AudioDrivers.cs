using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class AudioDrivers : IReadOnlyList<string>
    {
        internal AudioDrivers() { }

        public int Count => SDL_GetNumAudioDrivers();

        public unsafe string? Current
        {
            get
            {
                return UTF8ToString(SDL_GetCurrentAudioDriver());
            }
        }

        public unsafe string this[int index]
        {
            get
            {
                return UTF8ToString(SDL_GetAudioDriver(index)) ?? "";
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
