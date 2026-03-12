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
        if (!gotClientRect) {
            int getClientRectError = Marshal.GetLastPInvokeError();
            Debug.Assert(false, $"GetClientRect failed with error {getClientRectError}.");
            return false;
        }

        NativeMouseCaptureInterop.WinPoint topLeft = new NativeMouseCaptureInterop.WinPoint { X = clientRect.Left, Y = clientRect.Top };
        NativeMouseCaptureInterop.WinPoint bottomRight = new NativeMouseCaptureInterop.WinPoint { X = clientRect.Right, Y = clientRect.Bottom };

        Marshal.SetLastPInvokeError(0);
        bool topLeftConverted = NativeMouseCaptureInterop.ClientToScreen(windowHandle, ref topLeft);
        if (!topLeftConverted) {
            int topLeftError = Marshal.GetLastPInvokeError();
            Debug.Assert(false, $"ClientToScreen(top-left) failed with error {topLeftError}.");
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        bool bottomRightConverted = NativeMouseCaptureInterop.ClientToScreen(windowHandle, ref bottomRight);
        if (!bottomRightConverted) {
            int bottomRightError = Marshal.GetLastPInvokeError();
            Debug.Assert(false, $"ClientToScreen(bottom-right) failed with error {bottomRightError}.");
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
        if (previousCaptureHandle == IntPtr.Zero && setCaptureError != 0) {
            Debug.Assert(false, $"SetCapture failed with error {setCaptureError}.");
        }

        Marshal.SetLastPInvokeError(0);
        bool clipResult = NativeMouseCaptureInterop.ClipCursor(ref screenRect);
        if (!clipResult) {
            int clipError = Marshal.GetLastPInvokeError();
            Debug.Assert(false, $"ClipCursor failed to confine cursor to window rect with error {clipError}.");
        }

        _isCaptured = clipResult;
        return _isCaptured;
    }

    public static bool DisableCapture() {
        Marshal.SetLastPInvokeError(0);
        bool releaseResult = NativeMouseCaptureInterop.ReleaseCapture();
        int releaseError = Marshal.GetLastPInvokeError();
        if (!releaseResult && releaseError != 0) {
            Debug.Assert(false, $"ReleaseCapture failed with error {releaseError}.");
        }

        Marshal.SetLastPInvokeError(0);
        bool clipResult = NativeMouseCaptureInterop.ClipCursor(IntPtr.Zero);
        if (!clipResult) {
            int clipError = Marshal.GetLastPInvokeError();
            Debug.Assert(false, $"ClipCursor failed to release cursor confinement with error {clipError}.");
        }

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
