using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class JoystickHats : IReadOnlyList<JoyHatPosition>
    {
        readonly Joystick j;

        internal JoystickHats(Joystick j)
        {
            this.j = j;
        }

        public int Count => ErrorIfNegative(SDL_JoystickNumHats(j));

        public JoyHatPosition this[int index]
        {
            get
            {
                return SDL_JoystickGetHat(j, index);
            }
        }

        IEnumerable<JoyHatPosition> Enumerate()
        {
            int c = Count;
            for (int i = 0; i < c; ++i)
                yield return this[i];
        }

        public IEnumerator<JoyHatPosition> GetEnumerator()
        {
            return this.Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
