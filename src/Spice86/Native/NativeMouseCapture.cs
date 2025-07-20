using System;
using System.Runtime.InteropServices;

namespace Spice86.Native {
    internal static class NativeMouseCapture {
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly bool IsMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(ref Rect rect);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr rect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

        [DllImport("libSystem.dylib")]
        private static extern IntPtr CGMainDisplayID();

        [DllImport("libSystem.dylib")]
        private static extern void CGDisplayHideCursor(IntPtr display);

        [DllImport("libSystem.dylib")]
        private static extern void CGDisplayShowCursor(IntPtr display);

        [DllImport("libSystem.dylib")]
        private static extern void CGAssociateMouseAndMouseCursorPosition(bool connected);

        [DllImport("libX11.so.6")]
        private static extern int XGrabPointer(IntPtr display, IntPtr window, bool ownerEvents,
            int eventMask, int pointerMode, int keyboardMode, IntPtr confineTo, IntPtr cursor, ulong time);

        [DllImport("libX11.so.6")]
        private static extern int XUngrabPointer(IntPtr display, ulong time);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(string? display);

        [DllImport("libX11.so.6")]
        private static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XDefaultRootWindow(IntPtr display);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point {
            public int X;
            public int Y;
        }

        private const int GrabSuccess = 0;
        private const int GrabModeAsync = 1;
        private const int PointerMotionMask = 1 << 6;
        private const int ButtonPressMask = 1 << 2;
        private const int ButtonReleaseMask = 1 << 3;

        private static IntPtr x11Display = IntPtr.Zero;
        private static bool isCaptured;

        public static bool EnableCapture(IntPtr windowHandle) {
            if (windowHandle == IntPtr.Zero) {
                return false;
            }

            if (IsWindows) {
                if (!GetClientRect(windowHandle, out Rect clientRect)) {
                    return false;
                }

                Point topLeft = new Point { X = clientRect.Left, Y = clientRect.Top };
                Point bottomRight = new Point { X = clientRect.Right, Y = clientRect.Bottom };
                ClientToScreen(windowHandle, ref topLeft);
                ClientToScreen(windowHandle, ref bottomRight);

                Rect screenRect = new Rect {
                    Left = topLeft.X,
                    Top = topLeft.Y,
                    Right = bottomRight.X,
                    Bottom = bottomRight.Y
                };

                isCaptured = ClipCursor(ref screenRect);
                return isCaptured;
            }

            if (IsMacOs) {
                IntPtr mainDisplay = CGMainDisplayID();
                CGDisplayHideCursor(mainDisplay);
                CGAssociateMouseAndMouseCursorPosition(false);
                isCaptured = true;
                return true;
            }

            if (IsLinux) {
                isCaptured = TryGrabPointer(windowHandle);
                return isCaptured;
            }

            return false;
        }

        public static bool DisableCapture() {
            isCaptured = false;

            if (IsWindows) {
                return ClipCursor(IntPtr.Zero);
            }

            if (IsMacOs) {
                IntPtr mainDisplay = CGMainDisplayID();
                CGDisplayShowCursor(mainDisplay);
                CGAssociateMouseAndMouseCursorPosition(true);
                return true;
            }

            if (IsLinux && x11Display != IntPtr.Zero) {
                int result = XUngrabPointer(x11Display, 0);
                return result == GrabSuccess;
            }

            return false;
        }

        public static void Cleanup() {
            if (isCaptured) {
                DisableCapture();
            }

            if (IsLinux && x11Display != IntPtr.Zero) {
                XCloseDisplay(x11Display);
                x11Display = IntPtr.Zero;
            }
        }

        public static bool IsCaptured => isCaptured;

        private static bool TryGrabPointer(IntPtr windowHandle) {
            if (x11Display == IntPtr.Zero) {
                x11Display = XOpenDisplay(null);
            }

            if (x11Display == IntPtr.Zero) {
                return false;
            }

            int eventMask = PointerMotionMask | ButtonPressMask | ButtonReleaseMask;
            int result = XGrabPointer(
                x11Display,
                windowHandle,
                false,
                eventMask,
                GrabModeAsync,
                GrabModeAsync,
                IntPtr.Zero,
                IntPtr.Zero,
                0);

            return result == GrabSuccess;
        }
    }
}