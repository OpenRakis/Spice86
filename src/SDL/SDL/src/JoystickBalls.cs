using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class JoystickBalls : IReadOnlyList<Point>
    {
        readonly Joystick j;

        internal JoystickBalls(Joystick j)
        {
            this.j = j;
        }

        public int Count => ErrorIfNegative(SDL_JoystickNumBalls(j));

        public Point this[int index]
        {
            get
            {
                ErrorIfNegative(SDL_JoystickGetBall(j, index, out int dx, out int dy));
                return new Point(dx, dy);
            }
        }

        IEnumerable<Point> Enumerate()
        {
            int c = Count;
            for (int i = 0; i < c; ++i)
                yield return this[i];
        }

        public IEnumerator<Point> GetEnumerator()
        {
            return this.Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
