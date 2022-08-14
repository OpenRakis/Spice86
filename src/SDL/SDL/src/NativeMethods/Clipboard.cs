using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern /*char*/ byte* SDL_GetClipboardText();

        [DllImport(LibSDL2Name)]
        public static extern SDL_Bool SDL_HasClipboardText();

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetClipboardText(/*const char*/ byte* text);
    }
}
