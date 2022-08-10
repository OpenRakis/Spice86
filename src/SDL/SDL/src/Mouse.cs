using System;
using System.Text;
using System.Drawing;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    public static class Mouse
    {
        public static bool IsDown(int button)
        {
            uint state = SDL_GetMouseState(IntPtr.Zero, IntPtr.Zero);
            return (state & (1 << (button - 1))) != 0;
        }

        public static Point Position
        {
            get
            {
                int x, y;
                SDL_GetMouseState(out x, out y);
                return new Point(x, y);
            }
        }

        public static int X
        {
            get
            {
                int x;
                SDL_GetMouseState(out x, IntPtr.Zero);
                return x;
            }
        }

        public static int Y
        {
            get
            {
                int y;
                SDL_GetMouseState(IntPtr.Zero, out y);
                return y;
            }
        }

        public static Point GlobalPosition
        {
            get
            {
                int x, y;
                SDL_GetGlobalMouseState(out x, out y);
                return new Point(x, y);
            }
        }

        public static int GlobalX
        {
            get
            {
                int x;
                SDL_GetGlobalMouseState(out x, IntPtr.Zero);
                return x;
            }
        }

        public static int GlobalY
        {
            get
            {
                int y;
                SDL_GetGlobalMouseState(IntPtr.Zero, out y);
                return y;
            }
        }

        public static Point RelativePosition
        {
            get
            {
                int x, y;
                SDL_GetRelativeMouseState(out x, out y);
                return new Point(x, y);
            }
        }

        public static int RelativeX
        {
            get
            {
                int x;
                SDL_GetRelativeMouseState(out x, IntPtr.Zero);
                return x;
            }
        }

        public static int RelativeY
        {
            get
            {
                int y;
                SDL_GetRelativeMouseState(IntPtr.Zero, out y);
                return y;
            }
        }

        public static bool IsRelative
        {
            get
            {
                return SDL_GetRelativeMouseMode() == SDL_Bool.True;
            }
            set
            {
                SDL_SetRelativeMouseMode(value ? SDL_Bool.True : SDL_Bool.False);
            }
        }

        public static void Capture(bool capture)
        {
            ErrorIfNegative(SDL_CaptureMouse(capture ? SDL_Bool.True : SDL_Bool.False));
        }

        public static void WarpGlobal(int x, int y)
        {
            ErrorIfNegative(SDL_WarpMouseGlobal(x, y));
        }

        public static void WarpInWindow(int x, int y, Window? inWindow = null)
        {
            if (inWindow != null)
                SDL_WarpMouseInWindow(inWindow, x, y);
            else
                SDL_WarpMouseInWindow(IntPtr.Zero, x, y);
        }


        public static Window? Focused()
        {
            IntPtr ptr = SDL_GetMouseFocus();
            if (ptr != IntPtr.Zero)
                return new Window(ptr, false);
            else
                return null;
        }
    }
}
