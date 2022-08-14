using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport(LibSDL2Name)]
        public static extern int SDL_CaptureMouse(SDL_Bool enabled);

        [DllImport(LibSDL2Name)]
        public static extern Cursor SDL_CreateColorCursor(
            Surface surface, int hot_x, int hot_y);

        [DllImport(LibSDL2Name)]
        public static extern Cursor SDL_CreateCursor(
            /*const*/ byte* data,
            /*const*/ byte* mask,
            int w, int h,
            int hot_x, int hot_y);

        [DllImport(LibSDL2Name)]
        public static extern Cursor SDL_CreateSystemCursor(SystemCursor id);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_FreeCursor(IntPtr cursor);

        [DllImport(LibSDL2Name)]
        public static extern IntPtr SDL_GetCursor();

        [DllImport(LibSDL2Name)]
        public static extern IntPtr SDL_GetDefaultCursor();

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetGlobalMouseState(out int x, out int y);

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetGlobalMouseState(IntPtr x, out int y);

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetGlobalMouseState(out int x, IntPtr y);

        [DllImport(LibSDL2Name)]
        public static extern IntPtr SDL_GetMouseFocus();

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetMouseState(out int x, out int y);

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetMouseState(IntPtr x, out int y);

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetMouseState(out int x, IntPtr y);

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetMouseState(IntPtr x, IntPtr y);

        [DllImport(LibSDL2Name)]
        public static extern SDL_Bool SDL_GetRelativeMouseMode();

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetRelativeMouseState(out int x, out int y);

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetRelativeMouseState(IntPtr x, out int y);

        [DllImport(LibSDL2Name)]
        public static extern uint SDL_GetRelativeMouseState(out int x, IntPtr y);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetCursor(Cursor cursor);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetCursor(IntPtr cursor);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetRelativeMouseMode(SDL_Bool enabled);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_ShowCursor(int toggle);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_WarpMouseGlobal(int x, int y);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_WarpMouseInWindow(Window window, int x, int y);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_WarpMouseInWindow(IntPtr window, int x, int y);
    }

    public enum SystemCursor
    {
        Arrow,
        IBeam,
        Wait,
        Crosshair,
        WaitArrow,
        SizeSWSE,
        SizeNESW,
        SizeWE,
        SizeNS,
        SizeAll,
        No,
        Hand,
    }


}
