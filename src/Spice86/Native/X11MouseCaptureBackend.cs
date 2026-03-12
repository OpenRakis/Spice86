using System;
using System.Threading;

namespace Spice86.Native;

internal static class X11MouseCaptureBackend {
    private const int GrabSuccess = 0;
    private const int AlreadyGrabbed = 1;

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
        }

        if (_display == IntPtr.Zero) {
            return false;
        }

        if (_emptyCursor == IntPtr.Zero) {
            _emptyCursor = CreateEmptyCursor();
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

        NativeMouseCaptureInterop.XSync(_display, 0);

        return result == GrabSuccess;
    }

    public static bool DisableCapture() {
        if (_display == IntPtr.Zero) {
            return false;
        }

        int result = NativeMouseCaptureInterop.XUngrabPointer(_display, CurrentTime);

        NativeMouseCaptureInterop.XSync(_display, 0);

        _isCaptured = false;

        return result == GrabSuccess;
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
            NativeMouseCaptureInterop.XFreeCursor(_display, _emptyCursor);
            _emptyCursor = IntPtr.Zero;
        }

        NativeMouseCaptureInterop.XCloseDisplay(_display);
        _display = IntPtr.Zero;
        _isCaptured = false;
    }

    private static IntPtr CreateEmptyCursor() {
        IntPtr rootWindow = NativeMouseCaptureInterop.XDefaultRootWindow(_display);
        if (rootWindow == IntPtr.Zero) {
            return IntPtr.Zero;
        }

        byte[] data = new byte[1];
        IntPtr pixmap = NativeMouseCaptureInterop.XCreateBitmapFromData(_display, rootWindow, data, 1, 1);
        if (pixmap == IntPtr.Zero) {
            return IntPtr.Zero;
        }

        NativeMouseCaptureInterop.XColor black = new NativeMouseCaptureInterop.XColor();
        IntPtr cursor = NativeMouseCaptureInterop.XCreatePixmapCursor(_display, pixmap, pixmap, ref black, ref black, 0, 0);

        NativeMouseCaptureInterop.XFreePixmap(_display, pixmap);

        return cursor;
    }
}
