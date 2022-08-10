using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class GameControllerButtons : IReadOnlyList<bool>
    {
        readonly GameController j;

        internal GameControllerButtons(GameController j)
        {
            this.j = j;
        }

        public int Count => (int)GameControllerButton.Max;

        public bool this[int index] => this[(GameControllerButton)index];
        public bool this[GameControllerButton index]
        {
            get
            {
                bool b = SDL_GameControllerGetButton(j, index) == 1;
                if (!b)
                {
                    SDLException? err = GetError();
                    if (err != null) throw err;
                }
                return b;
            }
        }

        public GameControllerBind? GetBind(int index)
          => GetBind((GameControllerButton)index);
        public GameControllerBind? GetBind(GameControllerButton button)
        {
            var ret = GameControllerBind.Create(SDL_GameControllerGetBindForButton(j, button));
            if (ret == null)
            {
                SDLException? err = GetError();
                if (err != null) throw err;
            }
            return ret;
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
