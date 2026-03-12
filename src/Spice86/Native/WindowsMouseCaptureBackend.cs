using System;
using System.Diagnostics;
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
        Debug.Assert(gotClientRect, $"GetClientRect failed with error {Marshal.GetLastPInvokeError()}.");
        if (!gotClientRect) {
            return false;
        }

        NativeMouseCaptureInterop.WinPoint topLeft = new NativeMouseCaptureInterop.WinPoint { X = clientRect.Left, Y = clientRect.Top };
        NativeMouseCaptureInterop.WinPoint bottomRight = new NativeMouseCaptureInterop.WinPoint { X = clientRect.Right, Y = clientRect.Bottom };

        Marshal.SetLastPInvokeError(0);
        bool topLeftConverted = NativeMouseCaptureInterop.ClientToScreen(windowHandle, ref topLeft);
        Debug.Assert(topLeftConverted, $"ClientToScreen(top-left) failed with error {Marshal.GetLastPInvokeError()}.");
        if (!topLeftConverted) {
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        bool bottomRightConverted = NativeMouseCaptureInterop.ClientToScreen(windowHandle, ref bottomRight);
        Debug.Assert(bottomRightConverted, $"ClientToScreen(bottom-right) failed with error {Marshal.GetLastPInvokeError()}.");
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
        IntPtr previousCaptureHandle = NativeMouseCaptureInterop.SetCapture(windowHandle);
        int setCaptureError = Marshal.GetLastPInvokeError();
        Debug.Assert(previousCaptureHandle != IntPtr.Zero || setCaptureError == 0,
            $"SetCapture failed with error {Marshal.GetLastPInvokeError()}.");

        Marshal.SetLastPInvokeError(0);
        bool clipResult = NativeMouseCaptureInterop.ClipCursor(ref screenRect);
        Debug.Assert(clipResult, $"ClipCursor failed to confine cursor to window rect with error {Marshal.GetLastPInvokeError()}.");

        _isCaptured = clipResult;
        return _isCaptured;
    }

    public static bool DisableCapture() {
        Marshal.SetLastPInvokeError(0);
        bool releaseResult = NativeMouseCaptureInterop.ReleaseCapture();
        int releaseError = Marshal.GetLastPInvokeError();
        Debug.Assert(releaseResult || releaseError == 0,
            $"ReleaseCapture failed with error {releaseError}.");

        Marshal.SetLastPInvokeError(0);
        bool clipResult = NativeMouseCaptureInterop.ClipCursor(IntPtr.Zero);
        Debug.Assert(clipResult, $"ClipCursor failed to release cursor confinement with error {Marshal.GetLastPInvokeError()}.");

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
