using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class GameControllerAxes : IReadOnlyList<short>
    {
        readonly GameController j;

        internal GameControllerAxes(GameController j)
        {
            this.j = j;
        }

        public int Count => (int)GameControllerAxis.Max;

        public short this[int index] => this[(GameControllerAxis)index];
        public short this[GameControllerAxis index]
        {
            get
            {
                short v = SDL_GameControllerGetAxis(j, index);
                if (v == 0)
                {
                    SDLException? err = GetError();
                    if (err != null) throw err;
                }
                return v;
            }
        }

        public GameControllerBind? GetBind(int index)
          => GetBind((GameControllerAxis)index);
        public GameControllerBind? GetBind(GameControllerAxis axis)
        {
            var ret = GameControllerBind.Create(SDL_GameControllerGetBindForAxis(j, axis));
            if (ret == null)
            {
                SDLException? err = GetError();
                if (err != null) throw err;
            }
            return ret;
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
