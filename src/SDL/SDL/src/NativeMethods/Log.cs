using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern void SDL_LogSetAllPriority(LogPriority level);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_LogSetPriority(LogCategory category, LogPriority level);

        [DllImport(LibSDL2Name)]
        public static extern LogPriority SDL_LogGetPriority(LogCategory category);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_LogResetPriorities();

        [DllImport(LibSDL2Name, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_LogMessage(
          LogCategory category,
          LogPriority priority,
          /*const char*/ byte* fmt
        //__arglist
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_LogGetOutputFunction(
          out /*SDL_LogOutputFunction*/ IntPtr callback,
          out IntPtr userdata
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_LogSetOutputFunction(
          /*SDL_LogOutputFunction*/ IntPtr callback,
          IntPtr userdata
        );


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SDL_LogOutputFunction(
          IntPtr userdata,
          LogCategory category,
          LogPriority priority,
          /*const char*/ byte* message
        );

        public const int MAX_LOG_MESSAGE = 4096;
    }

    public enum LogPriority
    {
        Verbose = 1,
        Debug = 2,
        Info = 3,
        Warn = 4,
        Error = 5,
        Critical = 6,
    }

    public enum LogCategory
    {
        Application,
        Error,
        Assert,
        System,
        Audio,
        Video,
        Render,
        Input,
        Reserved1,
        Reserved2,
        Reserved3,
        Reserved4,
        Reserved5,
        Reserved6,
        Reserved7,
        Reserved8,
        Reserved9,
        Reserved10,
        Custom,
    }
}
