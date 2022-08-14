using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern SDL_Bool SDL_GetWindowWMInfo(
          Window window,
          out SDL_SysWMInfo info
        );

        public enum SysWMType
        {
            Unknown,
            Windows,
            X11,
            DirectFB,
            Cocoa,
            UIKit,
            Wayland,
            Mir,
            WinRT,
            Android,
            Vivante,
            OS2,
            Haiku,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_SysWMInfo
        {
            SDL_Version version;
            SysWMType subsystem;
            int dummy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_SysWMmsg
        {
            SDL_Version version;
            SysWMType subsystem;
            int dummy;
        }
    }
}
