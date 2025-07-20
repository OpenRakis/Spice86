using System;
using System.Runtime.InteropServices;

namespace Spice86.Native
{
    internal static class NativeMouseCapture
    {
        // Platform detection
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        
        #region Windows API
        [DllImport("user32.dll")]
        private static extern bool ClipCursor(ref RECT rect);
        
        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr rect);
        
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        
        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        
        [DllImport("user32.dll")]
        private static extern bool ShowCursor(bool bShow);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
        #endregion

        #region macOS API
        [DllImport("libSystem.dylib")]
        private static extern IntPtr CGMainDisplayID();
        
        [DllImport("libSystem.dylib")]
        private static extern void CGDisplayHideCursor(IntPtr display);
        
        [DllImport("libSystem.dylib")]
        private static extern void CGDisplayShowCursor(IntPtr display);
        
        [DllImport("libSystem.dylib")]
        private static extern void CGAssociateMouseAndMouseCursorPosition(bool connected);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint
        {
            public double X;
            public double Y;
        }
        #endregion

        #region Linux/X11 API
        [DllImport("libX11.so.6")]
        private static extern int XGrabPointer(IntPtr display, IntPtr window, bool ownerEvents,
            int eventMask, int pointerMode, int keyboardMode, IntPtr confineTo, IntPtr cursor, ulong time);
        
        [DllImport("libX11.so.6")]
        private static extern int XUngrabPointer(IntPtr display, ulong time);
        
        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(string display);
        
        [DllImport("libX11.so.6")]
        private static extern int XCloseDisplay(IntPtr display);
        
        [DllImport("libX11.so.6")]
        private static extern IntPtr XDefaultRootWindow(IntPtr display);
        
        private static IntPtr? x11Display = null;
        #endregion
        
        private static bool _isCaptured = false;
        
        /// <summary>
        /// Enable relative mouse mode using native methods
        /// </summary>
        public static bool EnableCapture(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero) return false;
            
            _isCaptured = true;
            
            if (IsWindows)
            {
                GetClientRect(windowHandle, out RECT rect);
                POINT p = new() { X = rect.Left, Y = rect.Top };
                ClientToScreen(windowHandle, ref p);
                rect.Left = p.X;
                rect.Top = p.Y;
                p.X = rect.Right;
                p.Y = rect.Bottom;
                ClientToScreen(windowHandle, ref p);
                rect.Right = p.X;
                rect.Bottom = p.Y;
                return ClipCursor(ref rect);
            }
            else if (IsMacOS)
            {
                IntPtr mainDisplay = CGMainDisplayID();
                CGDisplayHideCursor(mainDisplay);
                CGAssociateMouseAndMouseCursorPosition(false); // Relative mode
                return true;
            }
            else if (IsLinux)
            {
                if (x11Display == null)
                {
                    x11Display = XOpenDisplay(null!);
                }
                
                if (x11Display != IntPtr.Zero)
                {
                    const int GrabModeAsync = 1;
                    const int PointerMotionMask = 1 << 6;
                    const int ButtonPressMask = 1 << 2;
                    const int ButtonReleaseMask = 1 << 3;
                    
                    int eventMask = PointerMotionMask | ButtonPressMask | ButtonReleaseMask;
                    
                    int result = XGrabPointer(
                        x11Display.Value,
                        XDefaultRootWindow(x11Display.Value),
                        false,
                        eventMask,
                        GrabModeAsync,
                        GrabModeAsync,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        0);
                        
                    return result == 0; // GrabSuccess is 0
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Disable mouse capture and return to normal mode
        /// </summary>
        public static bool DisableCapture()
        {
            _isCaptured = false;
            
            if (IsWindows)
            {
                return ClipCursor(IntPtr.Zero);
            }
            else if (IsMacOS)
            {
                IntPtr mainDisplay = CGMainDisplayID();
                CGDisplayShowCursor(mainDisplay);
                CGAssociateMouseAndMouseCursorPosition(true); // Normal mode
                return true;
            }
            else if (IsLinux && x11Display != null)
            {
                int result = XUngrabPointer(x11Display.Value, 0);
                return result == 0;
            }
            
            return false;
        }
        
        /// <summary>
        /// Clean up any resources
        /// </summary>
        public static void Cleanup()
        {
            if (_isCaptured)
            {
                DisableCapture();
            }
            
            if (IsLinux && x11Display != null)
            {
                XCloseDisplay(x11Display.Value);
                x11Display = null;
            }
        }
        
        /// <summary>
        /// Check if mouse is currently captured
        /// </summary>
        public static bool IsCaptured => _isCaptured;
    }
}