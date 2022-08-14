using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern int SDL_Init(InitFlags flags);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_InitSubSystem(InitFlags flags);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_Quit();

        [DllImport(LibSDL2Name)]
        public static extern void SDL_QuitSubSystem(InitFlags flags);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetMainReady();

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_WasInit(InitFlags flags);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_WinRTRunApp(
          [MarshalAs(UnmanagedType.FunctionPtr)]
      MainFunction mainFunction,
          void* reserved
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void MainFunction(int argc, char** argv);
    }

    [Flags]
    public enum InitFlags : uint
    {
        Nothing = 0,
        Timer = 0x1,
        Audio = 0x10,
        Video = 0x20,
        Joystick = 0x200,
        Haptic = 0x1000,
        GameController = 0x2000,
        Events = 0x4000,
        Sensor = 0x8000,
        NoParachute = 0x10000,
        Everything = Timer | Audio | Video | Events | Joystick | Haptic | GameController | Sensor,
    }
}
