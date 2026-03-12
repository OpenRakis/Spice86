using System;
using System.Diagnostics;

namespace Spice86.Native;

internal static class MacOsMouseCaptureBackend {
    private const int CgErrorSuccess = 0;
    private static bool _isCaptured;

    public static bool EnableCapture() {
        uint mainDisplay = NativeMouseCaptureInterop.CGMainDisplayID();
        if (mainDisplay == 0) {
            Debug.Assert(false, "CGMainDisplayID returned 0.");
            _isCaptured = false;
            return false;
        }

        int hideResult = NativeMouseCaptureInterop.CGDisplayHideCursor(mainDisplay);
        if (hideResult != CgErrorSuccess) {
            Debug.Assert(false, $"CGDisplayHideCursor failed: {hideResult}.");
            _isCaptured = false;
            return false;
        }

        int associateResult = NativeMouseCaptureInterop.CGAssociateMouseAndMouseCursorPosition(false);
        if (associateResult != CgErrorSuccess) {
            Debug.Assert(false, $"CGAssociateMouseAndMouseCursorPosition(false) failed: {associateResult}.");
            _isCaptured = false;
            return false;
        }

        _isCaptured = true;
        return true;
    }

    public static bool DisableCapture() {
        uint mainDisplay = NativeMouseCaptureInterop.CGMainDisplayID();
        if (mainDisplay == 0) {
            Debug.Assert(false, "CGMainDisplayID returned 0.");
            _isCaptured = false;
            return false;
        }

        int showResult = NativeMouseCaptureInterop.CGDisplayShowCursor(mainDisplay);
        if (showResult != CgErrorSuccess) {
            Debug.Assert(false, $"CGDisplayShowCursor failed: {showResult}.");
            _isCaptured = false;
            return false;
        }

        int associateResult = NativeMouseCaptureInterop.CGAssociateMouseAndMouseCursorPosition(true);
        if (associateResult != CgErrorSuccess) {
            Debug.Assert(false, $"CGAssociateMouseAndMouseCursorPosition(true) failed: {associateResult}.");
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
