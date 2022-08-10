using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class AudioDevices : IReadOnlyList<string>
    {
        readonly int iscapture;

        internal AudioDevices(int iscapture)
        {
            this.iscapture = iscapture;
        }

        public int Count => SDL_GetNumAudioDevices(iscapture);

        public unsafe string this[int index]
        {
            get
            {
                return UTF8ToString(SDL_GetAudioDeviceName(index, iscapture)) ?? "";
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
