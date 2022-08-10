using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;

namespace SDLSharp
{
    public class JoystickDevice
    {
        readonly int index;

        internal JoystickDevice(int index)
        {
            this.index = index;
        }

        public int Index => index;
        public unsafe string Name => ErrorIfNull(UTF8ToString(SDL_JoystickNameForIndex(index))) ?? "";
        public unsafe Guid Guid
        {
            get
            {
                SDL_JoystickGUID g = SDL_JoystickGetDeviceGUID(index);
                var b = new Span<byte>(g.data, 16);
                if (b.SequenceEqual(Joystick.ZeroJoystickGUID))
                    throw GetError2();
                return new Guid(b);
            }
        }
        public bool IsController => SDL_IsGameController(index) == SDL_Bool.True;

        public Joystick Open()
        {
            return ErrorIfInvalid(SDL_JoystickOpen(index));
        }

        public GameController OpenController()
        {
            return ErrorIfInvalid(SDL_GameControllerOpen(index));
        }

        public override string ToString()
        {
            return $"JoystickDevice{Index} ({Guid}/{Name})";
        }
    }

    public class JoystickDevices : IReadOnlyList<JoystickDevice>
    {

        internal JoystickDevices()
        {
        }

        public int Count => ErrorIfNegative(SDL_NumJoysticks());

        public JoystickDevice this[int index] => new JoystickDevice(index);

        IEnumerable<JoystickDevice> Enumerate()
        {
            int c = Count;
            for (int i = 0; i < c; ++i)
                yield return this[i];
        }

        public IEnumerator<JoystickDevice> GetEnumerator()
        {
            return this.Enumerate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
