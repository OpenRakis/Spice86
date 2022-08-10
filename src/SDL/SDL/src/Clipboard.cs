using System;
using System.Text;
using System.Drawing;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public class Clipboard
    {
        public static unsafe string GetText()
        {
            byte* bytes = SDL_GetClipboardText();
            try
            {
                return ErrorIfNull(UTF8ToString(bytes))!;
            }
            finally
            {
                if (bytes != null)
                    SDL_free(bytes);
            }
        }

        public static unsafe void SetText(string text)
        {
            int l = SL(text);
            Span<byte> utf8 = l < 1024 ? stackalloc byte[l] : new byte[l];
            StringToUTF8(text, utf8);
            fixed (byte* p = &MemoryMarshal.GetReference(utf8))
                ErrorIfNegative(SDL_SetClipboardText(p));
        }

        public static bool HasText()
        {
            return SDL_HasClipboardText() == SDL_Bool.True;
        }
    }
}
