using System;
using System.Runtime.InteropServices;

namespace Spice86.Native;

internal static class WindowsMouseCaptureBackend {
    private static bool _isCaptured;

    public static bool EnableCapture(IntPtr windowHandle) {
        if (windowHandle == IntPtr.Zero) {
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        bool gotClientRect = NativeMouseCaptureInterop.GetClientRect(windowHandle, out NativeMouseCaptureInterop.ClipRect clientRect);
        if (!gotClientRect) {
            return false;
        }

        NativeMouseCaptureInterop.WinPoint topLeft = new NativeMouseCaptureInterop.WinPoint { X = clientRect.Left, Y = clientRect.Top };
        NativeMouseCaptureInterop.WinPoint bottomRight = new NativeMouseCaptureInterop.WinPoint { X = clientRect.Right, Y = clientRect.Bottom };

        Marshal.SetLastPInvokeError(0);
        bool topLeftConverted = NativeMouseCaptureInterop.ClientToScreen(windowHandle, ref topLeft);
        if (!topLeftConverted) {
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        bool bottomRightConverted = NativeMouseCaptureInterop.ClientToScreen(windowHandle, ref bottomRight);
        if (!bottomRightConverted) {
            return false;
        }

        NativeMouseCaptureInterop.ClipRect screenRect = new NativeMouseCaptureInterop.ClipRect {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = bottomRight.X,
            Bottom = bottomRight.Y
        };

        Marshal.SetLastPInvokeError(0);
        NativeMouseCaptureInterop.SetCapture(windowHandle);

        Marshal.SetLastPInvokeError(0);
        bool clipResult = NativeMouseCaptureInterop.ClipCursor(ref screenRect);

        _isCaptured = clipResult;
        return _isCaptured;
    }

    public static bool DisableCapture() {
        Marshal.SetLastPInvokeError(0);
        NativeMouseCaptureInterop.ReleaseCapture();

        Marshal.SetLastPInvokeError(0);
        bool clipResult = NativeMouseCaptureInterop.ClipCursor(IntPtr.Zero);

        _isCaptured = false;
        return clipResult;
    }

    public static void Cleanup() {
        if (_isCaptured) {
            DisableCapture();
        }

        _isCaptured = false;
    }
}
