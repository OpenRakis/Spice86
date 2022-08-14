using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetTicks();

        [DllImport(LibSDL2Name)]
        public static extern ulong SDL_GetPerformanceCounter();

        [DllImport(LibSDL2Name)]
        public static extern ulong SDL_GetPerformanceFrequency();

        [DllImport(LibSDL2Name)]
        public static extern int SDL_AddTimer(
          uint interval,
          /*SDL_TimerCallback*/ IntPtr callback,
          IntPtr param);

        [DllImport(LibSDL2Name)]
        public static extern SDL_Bool SDL_RemoveTimer(int id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate uint SDL_TimerCallback(uint interval, IntPtr param);
    }
}
