namespace Spice86.Native;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Windows user32.dll P/Invoke declarations used by <see cref="WindowsMouseCaptureBackend"/>.
/// </summary>
internal static partial class WindowsMouseCaptureInterop {
    /// <summary>Screen-coordinate rectangle used by <see cref="ClipCursor(ref ClipRect)"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ClipRect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>Point structure used by <see cref="ClientToScreen"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WinPoint {
        public int X;
        public int Y;
    }

    /// <summary>Confines the cursor to the supplied rectangle (screen coordinates).</summary>
    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "ClipCursor")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClipCursor(ref ClipRect rect);

    /// <summary>Removes the cursor clipping rectangle.</summary>
    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "ClipCursor")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClipCursor(IntPtr rect);

    /// <summary>Retrieves the client-area bounding rectangle of the specified window.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(IntPtr hWnd, out ClipRect lpRect);

    /// <summary>Converts a client-area coordinate to screen coordinates.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(IntPtr hWnd, ref WinPoint lpPoint);

    /// <summary>Sets the mouse capture to the specified window.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr SetCapture(IntPtr hWnd);

    /// <summary>Releases mouse capture from a window.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseCapture();
}
