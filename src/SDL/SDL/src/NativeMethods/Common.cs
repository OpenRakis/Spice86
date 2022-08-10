using System;
using System.Text;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        public enum SDL_Bool
        {
            False = 0,
            True = 1,
        }

        [DllImport("SDL2")]
        public static extern void SDL_free(void* ptr);

        public static string? UTF8ToString(byte* utf8)
        {
            if (utf8 == null)
                return null;
            int c = -1;
            while (utf8[++c] != '\0')
                ;
            return Encoding.UTF8.GetString(utf8, c);
        }

        public static int SL(string str)
        {
            return Encoding.UTF8.GetByteCount(str) + 1; // Null terminator
        }

        public static void StringToUTF8(string str, in Span<byte> utf8)
        {
#if DEBUG
            System.Diagnostics.Debug.Assert(utf8.Length > Encoding.UTF8.GetByteCount(str));
#endif
            int written = Encoding.UTF8.GetBytes(str, utf8);
            utf8[written] = 0;
        }
    }
}

