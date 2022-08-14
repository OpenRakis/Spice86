using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {

        [DllImport(LibSDL2Name)]
        public static extern int SDL_VideoInit(/*const char*/ byte* driver_name);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_VideoQuit();

        [DllImport(LibSDL2Name)]
        public static extern Window SDL_CreateWindow(
            /*const char*/ byte* title,
            int x,
            int y,
            int w,
            int h,
            WindowFlags flags
        );

        [DllImport(LibSDL2Name)]
        public static extern Window SDL_CreateWindowFrom(IntPtr data);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_CreateWindowAndRenderer(
            int width,
            int height,
            WindowFlags window_flags,
            out Window window,
            out Renderer renderer
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_DestroyWindow(IntPtr window);

        [DllImport(LibSDL2Name)]
        public static extern void SDL_DisableScreenSaver();

        [DllImport(LibSDL2Name)]
        public static extern void SDL_EnableScreenSaver();

        [DllImport(LibSDL2Name)]
        public static extern SDL_Bool SDL_IsScreenSaverEnabled();

        [DllImport(LibSDL2Name)]
        public static extern SDL_DisplayMode* SDL_GetClosestDisplayMode(
          int displayIndex,
          in SDL_DisplayMode mode,
          out SDL_DisplayMode closest
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetCurrentDisplayMode(int displayIndex, out SDL_DisplayMode mode);

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GetCurrentVideoDriver();

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetDesktopDisplayMode(
          int displayIndex,
          out SDL_DisplayMode mode
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetDisplayBounds(
          int displayIndex,
          out Rect rect
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetDisplayDPI(
          int displayIndex,
          out float ddpi,
          out float hdpi,
          out float vdpi
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetDisplayMode(
          int displayIndex,
          int modeIndex,
          out SDL_DisplayMode mode
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetDisplayUsableBounds(
          int displayIndex,
          out Rect rect
        );

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GetDisplayName(
          int displayIndex
        );

        [DllImport(LibSDL2Name)]
        public static extern IntPtr SDL_GetGrabbedWindow();

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetNumDisplayModes(int displayIndex);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetNumVideoDisplays();

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetNumVideoDrivers();

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GetVideoDriver(int index);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetWindowBordersSize(
          Window window,
          out int top,
          out int left,
          out int bottom,
          out int right
        );

        [DllImport(LibSDL2Name)]
        public static extern float SDL_GetWindowBrightness(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void* SDL_GetWindowData(
          Window window,
          /*const*/ char* name
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetWindowDisplayIndex(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetWindowDisplayMode(
          Window window,
          out SDL_DisplayMode mode
        );

        [DllImport(LibSDL2Name)]
        public static extern WindowFlags SDL_GetWindowFlags(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern IntPtr SDL_GetWindowFromID(UInt32 id);

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetWindowGammaRamp(
          Window window,
          ushort* red,
          ushort* green,
          ushort* blue
        );

        [DllImport(LibSDL2Name)]
        public static extern SDL_Bool SDL_GetWindowGrab(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern UInt32 SDL_GetWindowID(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_GetWindowMaximumSize(
          Window window,
          out int w,
          out int h
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_GetWindowMinimumSize(
          Window window,
          out int w,
          out int h
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_GetWindowOpacity(
          Window window,
          out float opacity
        );

        [DllImport(LibSDL2Name)]
        public static extern UInt32 SDL_GetWindowPixelFormat(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_GetWindowPosition(
          Window window,
          out int x,
          out int y
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_GetWindowSize(
          Window window,
          out int w,
          out int h
        );

        [DllImport(LibSDL2Name)]
        public static extern IntPtr SDL_GetWindowSurface(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern /*const char*/ byte* SDL_GetWindowTitle(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_HideWindow(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_ShowWindow(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_MaximizeWindow(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_MinimizeWindow(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_RaiseWindow(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_RestoreWindow(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowBordered(
          Window window,
          SDL_Bool bordered
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowBrightness(
          Window window,
          float brightness
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowData(
          Window window,
          /*const char*/ byte* name,
          void* userdata
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetWindowDisplayMode(
          Window window,
          SDL_DisplayMode* mode
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetWindowFullscreen(
          Window window,
          WindowFlags flags
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowGammaRamp(
          Window window,
          /*const*/ ushort* red,
          /*const*/ ushort* green,
          /*const*/ ushort* blue
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowGrab(
          Window window,
          SDL_Bool grabbed
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowHitTest(
          Window window,
          IntPtr callback, // SDL_HitTest
          IntPtr callback_data
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowIcon(
          Window window,
          Surface icon
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetWindowInputFocus(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowMaximumSize(
          Window window,
          int max_w,
          int max_h
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowMinimumSize(
          Window window,
          int min_w,
          int min_h
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_SetWindowModalFor(
          Window modal_window,
          Window parent_window
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowOpacity(
          Window window,
          float opacity
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowPosition(
          Window window,
          int x,
          int y
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowResizable(
          Window window,
          SDL_Bool resizable
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowSize(
          Window window,
          int w,
          int h
        );

        [DllImport(LibSDL2Name)]
        public static extern void SDL_SetWindowTitle(
          Window window,
          /*const char*/ byte* title
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_ShowMessageBox(
          in SDL_MessageBoxData messageboxdata,
          out int buttonid
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_ShowSimpleMessageBox(
          UInt32 flags,
          /*const char*/ byte* title,
          /*const char*/ byte* message,
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_ShowSimpleMessageBox(
          UInt32 flags,
          /*const char*/ byte* title,
          /*const char*/ byte* message,
          IntPtr window
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_UpdateWindowSurface(
          Window window
        );

        [DllImport(LibSDL2Name)]
        public static extern int SDL_UpdateWindowSurfaceRects(
          Window window,
          /*const*/ Rect* rects,
          int numrects
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate HitTestResult SDL_HitTest(
          IntPtr window,
          in Point area,
          IntPtr data
        );

        internal static readonly int WINDOWPOS_UNDEFINED = 0x1FFF0000;
        internal static readonly int WINDOWPOS_CENTERED = 0x2FFF0000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_DisplayMode
        {
            public UInt32 format;
            public int w, h;
            public int refresh_rate;
            public IntPtr driverdata;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_MessageBoxButtonData
        {
            public UInt32 flags;
            public int buttonid;
            public /*const char*/ byte* text;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_MessageBoxColorScheme
        {
            public fixed byte bytes[5 * 3];
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SDL_MessageBoxData
        {
            public UInt32 flags;
            public IntPtr window;
            public /*const char*/ byte* title;
            public /*const char*/ byte* message;
            public int numbuttons;
            public /*const*/ SDL_MessageBoxButtonData* buttons;
            public /*const*/ SDL_MessageBoxColorScheme* colorScheme;
        }
    }

    public enum HitTestResult
    {
        Normal,
        Draggable,
        ResizeTopLeft,
        RezizeTop,
        ResizeTopRight,
        ResizeBottom,
        ResizeBottomLeft,
        ResizeLeft,
    }

    public enum MessageBoxFlags : uint
    {
        Error = 0x10,
        Warning = 0x20,
        Information = 0x40,
    }

    [Flags]
    public enum WindowFlags : uint
    {
        None = 0,
        Fullscreen = 0x1,
        OpenGL = 0x2,
        Shown = 0x4,
        Hidden = 0x8,
        Borderless = 0x10,
        Resizable = 0x20,
        Minimized = 0x40,
        Maximized = 0x80,
        InputGrabbed = 0x100,
        InputFocus = 0x200,
        MouseFocus = 0x400,
        FullscreenDesktop = Fullscreen | 0x1000,
        Foreign = 0x800,
        AllowHighDPI = 0x2000,
        MouseCapture = 0x4000,
        AlwaysOnTop = 0x8000,
        SkipTaskbar = 0x10000,
        Utility = 0x20000,
        Tooltip = 0x40000,
        PopupMenu = 0x80000,
        Vulkan = 0x10000000,
        Metal = 0x20000000,
    }

    public enum DisplayOrientation
    {
        Unknown,
        Landscape,
        LandscapeFlipped,
        Portrait,
        PortraitFlipped,
    }
}
