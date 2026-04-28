using System;

namespace Spice86.Native;

internal static class MacOsMouseCaptureBackend {
    private const int CgErrorSuccess = 0;
    private static bool _isCaptured;

    public static bool EnableCapture() {
        uint mainDisplay = NativeMouseCaptureInterop.CGMainDisplayID();
        if (mainDisplay == 0) {
            _isCaptured = false;
            return false;
        }

        int hideResult = NativeMouseCaptureInterop.CGDisplayHideCursor(mainDisplay);
        if (hideResult != CgErrorSuccess) {
            _isCaptured = false;
            return false;
        }

        int associateResult = NativeMouseCaptureInterop.CGAssociateMouseAndMouseCursorPosition(false);
        if (associateResult != CgErrorSuccess) {
            _isCaptured = false;
            return false;
        }

        _isCaptured = true;
        return true;
    }

    public static bool DisableCapture() {
        uint mainDisplay = NativeMouseCaptureInterop.CGMainDisplayID();
        if (mainDisplay == 0) {
            _isCaptured = false;
            return false;
        }

        int showResult = NativeMouseCaptureInterop.CGDisplayShowCursor(mainDisplay);
        if (showResult != CgErrorSuccess) {
            _isCaptured = false;
            return false;
        }

        int associateResult = NativeMouseCaptureInterop.CGAssociateMouseAndMouseCursorPosition(true);
        if (associateResult != CgErrorSuccess) {
            _isCaptured = false;
            return false;
        }

        _isCaptured = false;
        return true;
    }

    public static void Cleanup() {
        if (_isCaptured) {
            DisableCapture();
        }

        _isCaptured = false;
    }
}
