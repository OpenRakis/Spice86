using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class GameControllerBind
    {
        internal SDL_GameControllerButtonBind bind;

        internal GameControllerBind(SDL_GameControllerButtonBind bind)
        {
            this.bind = bind;
        }

        internal static GameControllerBind? Create(in SDL_GameControllerButtonBind bind)
        {
            switch (bind.bindType)
            {
                default:
                case SDL_GameControllerBindType.None:
                    return null;
                case SDL_GameControllerBindType.Button:
                    return new GameControllerButtonBind(bind);
                case SDL_GameControllerBindType.Axis:
                    return new GameControllerAxisBind(bind);
                case SDL_GameControllerBindType.Hat:
                    return new GameControllerHatBind(bind);
            }
        }
    }

    public class GameControllerButtonBind : GameControllerBind
    {
        public int Button => bind.value.button;

        internal GameControllerButtonBind(SDL_GameControllerButtonBind bind) : base(bind)
        {
        }

        public override string ToString()
        {
            return $"b{Button}";
        }
    }

    public class GameControllerAxisBind : GameControllerBind
    {
        public int Axis => bind.value.axis;

        internal GameControllerAxisBind(SDL_GameControllerButtonBind bind) : base(bind)
        {
        }

        public override string ToString()
        {
            return $"a{Axis}";
        }
    }

    public class GameControllerHatBind : GameControllerBind
    {
        public int Hat => bind.value.hat.hat;
        public int Mask => bind.value.hat.hat_mask;

        internal GameControllerHatBind(SDL_GameControllerButtonBind bind) : base(bind)
        {
        }

        public override string ToString()
        {
            return $"h{Hat}.{Mask}";
        }
    }
}
