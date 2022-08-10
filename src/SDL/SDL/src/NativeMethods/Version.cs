using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static unsafe partial class NativeMethods
    {
        [DllImport("SDL2")]
        public static extern /*const char*/byte* SDL_GetRevision();

        [DllImport("SDL2")]
        public static extern int SDL_GetRevisionNumber();

        [DllImport("SDL2")]
        public static extern void SDL_GetVersion(out SDL_Version ver);

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_Version
        {
            public byte major;
            public byte minor;
            public byte patch;
        }
    }
}
