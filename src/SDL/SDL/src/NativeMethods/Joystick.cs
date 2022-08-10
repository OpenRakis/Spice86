using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport("SDL2")]
        public static extern int SDL_NumJoysticks();

        [DllImport("SDL2")]
        public static extern /*const char*/ byte* SDL_JoystickNameForIndex(int device_index);

        [DllImport("SDL2")]
        public static extern Joystick SDL_JoystickOpen(int device_index);

        [DllImport("SDL2")]
        public static extern Joystick SDL_JoystickFromInstanceID(int joyid);

        [DllImport("SDL2")]
        public static extern /*const char*/ byte* SDL_JoystickName(Joystick joystick);

        [DllImport("SDL2")]
        public static extern SDL_JoystickGUID SDL_JoystickGetDeviceGUID(int device_index);

        [DllImport("SDL2")]
        public static extern SDL_JoystickGUID SDL_JoystickGetGUID(Joystick joystick);

        [DllImport("SDL2")]
        public static extern void SDL_JoystickGetGUIDString(SDL_JoystickGUID guid, /*char*/ byte* pszGUID, int cbGUID);

        [DllImport("SDL2")]
        public static extern SDL_JoystickGUID SDL_JoystickGetGUIDFromString(/*const char*/ byte* pchGUID);

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_JoystickGetAttached(Joystick joystick);

        [DllImport("SDL2")]
        public static extern int SDL_JoystickInstanceID(Joystick joystick);

        [DllImport("SDL2")]
        public static extern int SDL_JoystickNumAxes(Joystick joystick);

        [DllImport("SDL2")]
        public static extern int SDL_JoystickNumBalls(Joystick joystick);

        [DllImport("SDL2")]
        public static extern int SDL_JoystickNumHats(Joystick joystick);

        [DllImport("SDL2")]
        public static extern int SDL_JoystickNumButtons(Joystick joystick);

        [DllImport("SDL2")]
        public static extern void SDL_JoystickUpdate();

        [DllImport("SDL2")]
        public static extern int SDL_JoystickEventState(int state);

        [DllImport("SDL2")]
        public static extern short SDL_JoystickGetAxis(Joystick joystick, int axis);

        [DllImport("SDL2")]
        public static extern JoyHatPosition SDL_JoystickGetHat(Joystick joystick, int hat);

        [DllImport("SDL2")]
        public static extern int SDL_JoystickGetBall(Joystick joystick, int ball, out int dx, out int dy);

        [DllImport("SDL2")]
        public static extern byte SDL_JoystickGetButton(Joystick joystick, int button);

        [DllImport("SDL2")]
        public static extern void SDL_JoystickClose(IntPtr joystick);

        [DllImport("SDL2")]
        public static extern JoystickPowerLevel SDL_JoystickCurrentPowerLevel(Joystick joystick);


        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_JoystickGUID
        {
            public fixed byte data[16];
        }
    }

    public enum JoystickPowerLevel
    {
        Unknown = -1,
        Empty,
        Low,
        Medium,
        Full,
        Wired,
        Max,
    }

    [Flags]
    public enum JoyHatPosition : byte
    {
        Centered = 0,
        Up = 1,
        Right = 2,
        Down = 4,
        Left = 8,
        RightUp = Right | Up,
        RightDown = Right | Down,
        LeftUp = Left | Up,
        LeftDown = Left | Down,
    }

}
