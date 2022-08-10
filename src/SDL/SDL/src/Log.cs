using System;
using System.Collections.Generic;
using System.Text;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static class Log
    {
        public static LogPriorities Priorities { get; } = new LogPriorities();

        public static unsafe void Message(LogCategory category, LogPriority priority, string message)
        {
            message = message.Replace("%", "%%");
            Span<byte> b = stackalloc byte[SL(message)];
            StringToUTF8(message, b);
            fixed (byte* p = b)
                SDL_LogMessage(category, priority, p);
        }

        static SDL_LogOutputFunction? f;
        static IntPtr outpPtr;
        static LogOutputFunction? f2;

        public static unsafe LogOutputFunction? OutputFunction
        {
            get
            {
                IntPtr func, ud;
                SDL_LogGetOutputFunction(out func, out ud);
                if (func == IntPtr.Zero)
                    return null;
                else if (func == outpPtr)
                    return f2;
                else
                    return NativeLogOutputFunction;
            }
            set
            {
                if (value != null)
                {
                    f2 = value;
                    SDL_LogOutputFunction outf = (IntPtr ud, LogCategory cat, LogPriority prio, byte* msg) =>
                    {
                        try
                        {
                            value(cat, prio, UTF8ToString(msg));
                        }
                        catch (Exception ex)
                        {
                            SDL.OnUnhandledException(ex, false);
                        }
                    };
                    outpPtr = Marshal.GetFunctionPointerForDelegate(outf);
                    SDL_LogSetOutputFunction(outpPtr, IntPtr.Zero);
                    f = outf;
                }
                else
                {
                    SDL_LogSetOutputFunction(IntPtr.Zero, IntPtr.Zero);
                    f = null;
                    f2 = null;
                    outpPtr = IntPtr.Zero;
                }
            }
        }

        static unsafe void NativeLogOutputFunction(LogCategory cat, LogPriority prio, string? msg)
        {
            IntPtr myf, ud2;
            SDL_LogGetOutputFunction(out myf, out ud2);
            if (myf == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(NativeLogOutputFunction));
            SDL_LogOutputFunction? dlg = Marshal.GetDelegateForFunctionPointer<SDL_LogOutputFunction>(myf);
            unsafe
            {
                if (msg != null)
                {
                    Span<byte> buf = stackalloc byte[SL(msg)];
                    StringToUTF8(msg, buf);
                    fixed (byte* ptr = buf)
                        dlg(ud2, cat, prio, ptr);
                }
                else
                {
                    dlg(ud2, cat, prio, null);
                }
            }
        }
    }

    public delegate void LogOutputFunction(LogCategory category, LogPriority priority, string? message);

    public class LogPriorities
    {
        internal LogPriorities() { }

        public LogPriority this[LogCategory category]
        {
            get => SDL_LogGetPriority(category);
            set => SDL_LogSetPriority(category, value);
        }

        public void Reset()
        {
            SDL_LogResetPriorities();
        }

        public void SetAll(LogPriority level)
        {
            SDL_LogSetAllPriority(level);
        }
    }
}
