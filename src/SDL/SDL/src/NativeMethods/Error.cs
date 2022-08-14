using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern /*const*/byte* SDL_GetError();

        [DllImport(LibSDL2Name)]
        public static extern byte* SDL_GetErrorMsg(byte* output, int maxlen);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetError(/*const char*/ byte* fmt/*, __arglist*/);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_ClearError();

        public static void SetError(Exception ex)
        {
            SetError(ex.ToString());
        }

        static bool getErrorMsgAvailable = true;
        private static unsafe string? GetErrorStr()
        {
            if (getErrorMsgAvailable)
            {
                try
                {
                    Span<byte> buf = stackalloc byte[4096];
                    buf[0] = 0;
                    fixed (byte* p = buf)
                        SDL_GetErrorMsg(p, buf.Length);
                    return System.Text.Encoding.UTF8.GetString(buf.Slice(0, buf.IndexOf((byte)0)));
                }
                catch (EntryPointNotFoundException)
                {
                    getErrorMsgAvailable = false;
                }
            }
            return UTF8ToString(SDL_GetError());
        }

        public static unsafe void SetError(string error)
        {
            error = error.Replace("%", "%%");
            Span<byte> buf = stackalloc byte[SL(error)];
            fixed (byte* ptr = buf)
                SDL_SetError(ptr);
        }

        public static SDLException? GetError()
        {
            string? err = GetErrorStr();
            if (err == null || err == "")
                return null;
            return new SDLException(err);
        }

        public static SDLException GetError2()
        {
            return GetError() ?? new SDLException("An error was expected, but there was none!");
        }

        public static void Throw()
        {
            throw GetError2();
        }

        public static UInt32 ErrorIfZero(UInt32 val)
        {
            if (val == 0)
                Throw();
            return val;
        }

        public static int ErrorIfZero(int val)
        {
            if (val == 0)
                Throw();
            return val;
        }
        public static UInt32 ErrorIfNonZero(UInt32 val)
        {
            if (val != 0)
                Throw();
            return val;
        }

        public static int ErrorIfNegative(int val)
        {
            if (val < 0)
                Throw();
            return val;
        }

        public static T ErrorIfNull<T>(T val)
        {
            if (val == null)
                Throw();
            return val;
        }

        public static IntPtr ErrorIfNull(IntPtr val)
        {
            if (val == IntPtr.Zero)
                Throw();
            return val;
        }

        public static T ErrorIfInvalid<T>(T val) where T : SafeHandle
        {
            if (val.IsInvalid)
                Throw();
            return val;
        }

        public static SDL_Bool ErrorIfFalse(SDL_Bool val)
        {
            if (val != SDL_Bool.True)
                Throw();
            return val;
        }

    }
}
