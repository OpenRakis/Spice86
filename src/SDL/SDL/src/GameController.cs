using System;
using System.Text;
using System.Drawing;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class GameController : SafeHandle
    {
        public static JoystickDevices Devices { get; } = new JoystickDevices();

        public GameControllerAxes Axes { get; }
        public GameControllerButtons Buttons { get; }

        protected GameController() : base(IntPtr.Zero, true)
        {
            Axes = new GameControllerAxes(this);
            Buttons = new GameControllerButtons(this);
        }

        public GameController(IntPtr h, bool owned) : base(IntPtr.Zero, owned)
        {
            SetHandle(h);
            Axes = new GameControllerAxes(this);
            Buttons = new GameControllerButtons(this);
        }

        public GameController(int id) : this()
        {
            GameController? gm = ErrorIfInvalid(SDL_GameControllerFromInstanceId(id));
            SetHandle(gm.handle);
            gm.SetHandle(IntPtr.Zero);
        }

        public unsafe string Name => ErrorIfNull(UTF8ToString(SDL_GameControllerName(this))) ?? "";

        public bool IsAttached
        {
            get
            {
                bool b = SDL_GameControllerGetAttached(this) == SDL_Bool.True;
                if (!b)
                {
                    SDLException? err = GetError();
                    if (err != null) throw err;
                }
                return b;
            }
        }

        public Joystick Joystick => new Joystick(SDL_GameControllerGetJoystick(this), false);

        public GameControllerMapping Mapping => ErrorIfInvalid(SDL_GameControllerMapping(this));

        public static unsafe bool AddMappingsFromString(string mappings)
        {
            Span<byte> mapSpan = new byte[SL(mappings)];
            fixed (byte* p = mapSpan)
                return ErrorIfNegative(SDL_GameControllerAddMapping(p)) != 0;
        }

        public static unsafe bool AddMappingFromString(RWOps ops)
        {
            return ErrorIfNegative(SDL_GameControllerAddMappingsFromRW(ops, 0)) != 0;
        }

        public static unsafe bool AddMappingFromFile(string file)
        {
            return ErrorIfNegative(SDL_GameControllerAddMappingsFromRW(RWOps.FromFile(file, "rb"), 1)) != 0;
        }

        public static unsafe GameControllerAxis AxisFromName(string s)
        {
            Span<byte> buf = stackalloc byte[SL(s)];
            StringToUTF8(s, buf);
            GameControllerAxis a;
            fixed (byte* b = buf)
                a = SDL_GameControllerGetAxisFromString(b);
            if (a == GameControllerAxis.Invalid)
            {
                SDLException? err = GetError();
                if (err != null) throw err;
            }
            return a;
        }

        public static unsafe string AxisName(GameControllerAxis b)
          => ErrorIfNull(UTF8ToString(SDL_GameControllerGetStringForAxis(b))) ?? "";

        public static unsafe GameControllerButton ButtonFromName(string s)
        {
            Span<byte> buf = stackalloc byte[SL(s)];
            StringToUTF8(s, buf);
            GameControllerButton a;
            fixed (byte* b = buf)
                a = SDL_GameControllerGetButtonFromString(b);
            if (a == GameControllerButton.Invalid)
            {
                SDLException? err = GetError();
                if (err != null) throw err;
            }
            return a;
        }

        public static unsafe string ButtonName(GameControllerButton b)
          => ErrorIfNull(UTF8ToString(SDL_GameControllerGetStringForButton(b))) ?? "";

        public static void Update()
        {
            SDL_GameControllerUpdate();
        }

        public static bool EventsEnabled
        {
            get
            {
                return ErrorIfNegative(SDL_GameControllerEventState(-1)) == 1;
            }
            set
            {
                ErrorIfNegative(SDL_GameControllerEventState(value ? 0 : 1));
            }
        }

        public override string ToString()
        {
            return Joystick.ToString().Replace("Joystick", "GameController");
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        override protected bool ReleaseHandle()
        {
            NativeMethods.SDL_GameControllerClose(this.handle);
            return true;
        }
    }
}
