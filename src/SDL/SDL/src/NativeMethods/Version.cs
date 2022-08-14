using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern /*const char*/byte* SDL_GetRevision();

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetRevisionNumber();

        [DllImport(LibSDL2Name)]
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
