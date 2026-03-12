using System;
using System.Diagnostics;
using System.Threading;

namespace Spice86.Native;

internal static class X11MouseCaptureBackend {
    private const int GrabSuccess = 0;
    private const int AlreadyGrabbed = 1;
    private const int GrabInvalidTime = 2;
    private const int GrabNotViewable = 3;
    private const int GrabFrozen = 4;

    private const int GrabModeAsync = 1;
    private const int PointerMotionMask = 1 << 6;
    private const int ButtonPressMask = 1 << 2;
    private const int ButtonReleaseMask = 1 << 3;
    private const int FocusChangeMask = 1 << 21;

    private const int GrabAttemptCount = 100;
    private const int GrabAttemptDelayMilliseconds = 50;
    private const ulong CurrentTime = 0;

    private static IntPtr _display = IntPtr.Zero;
    private static IntPtr _emptyCursor = IntPtr.Zero;
    private static bool _isCaptured;

    public static bool EnableCapture(IntPtr windowHandle) {
        if (windowHandle == IntPtr.Zero) {
            return false;
        }

        if (_display == IntPtr.Zero) {
            _display = NativeMouseCaptureInterop.XOpenDisplay(IntPtr.Zero);
            Debug.Assert(_display != IntPtr.Zero, "XOpenDisplay failed. DISPLAY might be unavailable.");
        }

        if (_display == IntPtr.Zero) {
            return false;
        }

        if (_emptyCursor == IntPtr.Zero) {
            _emptyCursor = CreateEmptyCursor();
            Debug.Assert(_emptyCursor != IntPtr.Zero, "Failed to create empty X11 cursor.");
        }

        if (_emptyCursor == IntPtr.Zero) {
            return false;
        }

        int eventMask = PointerMotionMask | ButtonPressMask | ButtonReleaseMask | FocusChangeMask;
        int result = AlreadyGrabbed;

        // SDL-style retry loop: X server can temporarily refuse pointer grab.
        for (int attempt = 0; attempt < GrabAttemptCount; attempt++) {
            result = NativeMouseCaptureInterop.XGrabPointer(
                _display,
                windowHandle,
                0,
                eventMask,
                GrabModeAsync,
                GrabModeAsync,
                windowHandle,
                _emptyCursor,
                CurrentTime);

            if (result == GrabSuccess) {
                _isCaptured = true;
                break;
            }

            Thread.Sleep(GrabAttemptDelayMilliseconds);
        }

        int syncResult = NativeMouseCaptureInterop.XSync(_display, 0);
        Debug.Assert(syncResult == 0, $"XSync failed after grab: {syncResult}.");
        Debug.Assert(result == GrabSuccess, $"XGrabPointer failed: {GetGrabResultName(result)} ({result}).");

        return result == GrabSuccess;
    }

    public static bool DisableCapture() {
        if (_display == IntPtr.Zero) {
            return false;
        }

        int result = NativeMouseCaptureInterop.XUngrabPointer(_display, CurrentTime);
        Debug.Assert(result == GrabSuccess, $"XUngrabPointer failed: {GetGrabResultName(result)} ({result}).");

        int syncResult = NativeMouseCaptureInterop.XSync(_display, 0);
        Debug.Assert(syncResult == 0, $"XSync failed after ungrab: {syncResult}.");

        bool success = result == GrabSuccess;
        if (success) {
            _isCaptured = false;
        }

        return success;
    }

    public static void Cleanup() {
        if (_display == IntPtr.Zero) {
            _isCaptured = false;
            return;
        }

        if (_isCaptured) {
            DisableCapture();
        }

        if (_emptyCursor != IntPtr.Zero) {
            int freeCursorResult = NativeMouseCaptureInterop.XFreeCursor(_display, _emptyCursor);
            Debug.Assert(freeCursorResult == 0, $"XFreeCursor failed: {freeCursorResult}.");
            _emptyCursor = IntPtr.Zero;
        }

        int closeDisplayResult = NativeMouseCaptureInterop.XCloseDisplay(_display);
        Debug.Assert(closeDisplayResult == 0, $"XCloseDisplay failed: {closeDisplayResult}.");
        _display = IntPtr.Zero;
        _isCaptured = false;
    }

    private static IntPtr CreateEmptyCursor() {
        IntPtr rootWindow = NativeMouseCaptureInterop.XDefaultRootWindow(_display);
        Debug.Assert(rootWindow != IntPtr.Zero, "XDefaultRootWindow failed.");
        if (rootWindow == IntPtr.Zero) {
            return IntPtr.Zero;
        }

        byte[] data = new byte[1];
        IntPtr pixmap = NativeMouseCaptureInterop.XCreateBitmapFromData(_display, rootWindow, data, 1, 1);
        Debug.Assert(pixmap != IntPtr.Zero, "XCreateBitmapFromData failed for empty cursor pixmap.");
        if (pixmap == IntPtr.Zero) {
            return IntPtr.Zero;
        }

        NativeMouseCaptureInterop.XColor black = new NativeMouseCaptureInterop.XColor();
        IntPtr cursor = NativeMouseCaptureInterop.XCreatePixmapCursor(_display, pixmap, pixmap, ref black, ref black, 0, 0);

        int freePixmapResult = NativeMouseCaptureInterop.XFreePixmap(_display, pixmap);
        Debug.Assert(freePixmapResult == 0, $"XFreePixmap failed: {freePixmapResult}.");
        Debug.Assert(cursor != IntPtr.Zero, "XCreatePixmapCursor failed.");

        return cursor;
    }

    private static string GetGrabResultName(int grabResult) {
        return grabResult switch {
            GrabSuccess => nameof(GrabSuccess),
            AlreadyGrabbed => nameof(AlreadyGrabbed),
            GrabInvalidTime => nameof(GrabInvalidTime),
            GrabNotViewable => nameof(GrabNotViewable),
            GrabFrozen => nameof(GrabFrozen),
            _ => $"Unknown({grabResult})"
        };
    }
}
