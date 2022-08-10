using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class JoystickAxes : IReadOnlyList<short>
    {
        readonly Joystick j;

        internal JoystickAxes(Joystick j)
        {
            this.j = j;
        }

        public int Count => ErrorIfNegative(SDL_JoystickNumAxes(j));

        public short this[int index]
        {
            get
            {
                short v = SDL_JoystickGetAxis(j, index);
                if (v == 0)
                {
                    SDLException? err = GetError();
                    if (err != null) throw err;
                }
                return v;
            }
        }

        IEnumerable<short> Enumerate()
        {
            int c = Count;
            for (int i = 0; i < c; ++i)
                yield return this[i];
        }

        public IEnumerator<short> GetEnumerator()
        {
            return this.Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
