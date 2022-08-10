using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class JoystickButtons : IReadOnlyList<bool>
    {
        readonly Joystick j;

        internal JoystickButtons(Joystick j)
        {
            this.j = j;
        }

        public int Count => ErrorIfNegative(SDL_JoystickNumButtons(j));

        public bool this[int index]
        {
            get
            {
                return SDL_JoystickGetButton(j, index) == 1;
            }
        }

        IEnumerable<bool> Enumerate()
        {
            int c = Count;
            for (int i = 0; i < c; ++i)
                yield return this[i];
        }

        public IEnumerator<bool> GetEnumerator()
        {
            return this.Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
