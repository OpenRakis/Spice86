using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class VideoDrivers : IReadOnlyList<string>
    {
        internal VideoDrivers() { }

        public int Count => SDL_GetNumVideoDrivers();

        public unsafe string? Current
        {
            get
            {
                return UTF8ToString(SDL_GetCurrentVideoDriver());
            }
        }

        public unsafe string this[int index]
        {
            get
            {
                return UTF8ToString(SDL_GetVideoDriver(index)) ?? "";
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
