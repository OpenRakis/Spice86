using System;
using System.Runtime.InteropServices;

namespace Spice86.Native;

internal static partial class NativeMouseCaptureInterop {
    [StructLayout(LayoutKind.Sequential)]
    internal struct ClipRect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WinPoint {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XColor {
        public ulong Pixel;
        public ushort Red;
        public ushort Green;
        public ushort Blue;
        public byte Flags;
        public byte Pad;
    }

    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "ClipCursor")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClipCursor(ref ClipRect rect);

    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "ClipCursor")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClipCursor(IntPtr rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(IntPtr hWnd, out ClipRect lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(IntPtr hWnd, ref WinPoint lpPoint);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr SetCapture(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseCapture();

    [LibraryImport("CoreGraphics", SetLastError = false)]
    internal static partial uint CGMainDisplayID();

    [LibraryImport("CoreGraphics", SetLastError = false)]
    internal static partial int CGDisplayHideCursor(uint display);

    [LibraryImport("CoreGraphics", SetLastError = false)]
    internal static partial int CGDisplayShowCursor(uint display);

    [LibraryImport("CoreGraphics", SetLastError = false)]
    internal static partial int CGAssociateMouseAndMouseCursorPosition([MarshalAs(UnmanagedType.Bool)] bool connected);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial int XGrabPointer(IntPtr display, IntPtr window, int ownerEvents,
        int eventMask, int pointerMode, int keyboardMode, IntPtr confineTo, IntPtr cursor, ulong time);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial int XUngrabPointer(IntPtr display, ulong time);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial IntPtr XOpenDisplay(IntPtr displayName);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial int XCloseDisplay(IntPtr display);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial IntPtr XDefaultRootWindow(IntPtr display);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial int XSync(IntPtr display, int discard);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial IntPtr XCreateBitmapFromData(IntPtr display, IntPtr drawable, byte[] data, int width, int height);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial IntPtr XCreatePixmapCursor(IntPtr display, IntPtr source, IntPtr mask,
        ref XColor foreColor, ref XColor backColor, int x, int y);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial int XFreeCursor(IntPtr display, IntPtr cursor);

    [LibraryImport("libX11.so.6", SetLastError = false)]
    internal static partial int XFreePixmap(IntPtr display, IntPtr pixmap);
}
