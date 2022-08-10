using System;
using System.Runtime.InteropServices;
using static SDLSharp.NativeMethods;
using static SDLSharp.TTFNativeMethods;

namespace SDLSharp
{
    public static class TTF
    {
        public static bool Initialized => TTF_WasInit() != 0;

        public static void Init()
        {
            ErrorIfNegative(TTF_Init());
        }

        public static void Quit()
        {
            TTF_Quit();
        }

        public static void SetByteSwap(bool enabled)
        {
            TTF_ByteSwappedUNICODE(enabled ? 1 : 0);
        }

        public static unsafe Version RuntimeVersion
        {
            get
            {
                SDL_Version* v = TTF_Linked_Version();
                return new Version(v->major, v->minor, v->patch, 0);
            }
        }

        public static unsafe TTFFont OpenFont(string file, int ptSize)
        {
            Span<byte> buf = stackalloc byte[SL(file)];
            StringToUTF8(file, buf);
            fixed (byte* b = buf)
                return ErrorIfInvalid(TTF_OpenFont(b, ptSize));
        }

        public static unsafe TTFFont OpenFont(string file, int ptSize, long index)
        {
            Span<byte> buf = stackalloc byte[SL(file)];
            StringToUTF8(file, buf);
            fixed (byte* b = buf)
                return ErrorIfInvalid(TTF_OpenFontIndex(b, ptSize, index));
        }

        public static TTFFont OpenFont(RWOps ops, int ptSize)
        {
            return ErrorIfInvalid(TTF_OpenFontRW(ops, 0, ptSize));
        }

        public static TTFFont OpenFont(RWOps ops, int ptSize, long index)
        {
            return ErrorIfInvalid(TTF_OpenFontIndexRW(ops, 0, ptSize, index));
        }
    }
}
