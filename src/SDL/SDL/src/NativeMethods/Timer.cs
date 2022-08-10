using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport("SDL2")]
        public static extern uint SDL_GetTicks();

        [DllImport("SDL2")]
        public static extern ulong SDL_GetPerformanceCounter();

        [DllImport("SDL2")]
        public static extern ulong SDL_GetPerformanceFrequency();

        [DllImport("SDL2")]
        public static extern int SDL_AddTimer(
          uint interval,
          /*SDL_TimerCallback*/ IntPtr callback,
          IntPtr param);

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_RemoveTimer(int id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate uint SDL_TimerCallback(uint interval, IntPtr param);
    }
}
