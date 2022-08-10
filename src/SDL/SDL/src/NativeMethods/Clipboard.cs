using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport("SDL2")]
        public static extern /*char*/ byte* SDL_GetClipboardText();

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_HasClipboardText();

        [DllImport("SDL2")]
        public static extern int SDL_SetClipboardText(/*const char*/ byte* text);
    }
}
