using System;
using System.Diagnostics;

namespace Spice86.Native;

internal static class MacOsMouseCaptureBackend {
    private const int CgErrorSuccess = 0;
    private static bool _isCaptured;

    public static bool EnableCapture() {
        uint mainDisplay = NativeMouseCaptureInterop.CGMainDisplayID();
        Debug.Assert(mainDisplay != 0, "CGMainDisplayID returned 0.");

        int hideResult = NativeMouseCaptureInterop.CGDisplayHideCursor(mainDisplay);
        Debug.Assert(hideResult == CgErrorSuccess, $"CGDisplayHideCursor failed: {hideResult}.");

        int associateResult = NativeMouseCaptureInterop.CGAssociateMouseAndMouseCursorPosition(false);
        Debug.Assert(associateResult == CgErrorSuccess,
            $"CGAssociateMouseAndMouseCursorPosition(false) failed: {associateResult}.");

        _isCaptured = hideResult == CgErrorSuccess && associateResult == CgErrorSuccess;
        return _isCaptured;
    }

    public static bool DisableCapture() {
        uint mainDisplay = NativeMouseCaptureInterop.CGMainDisplayID();
        Debug.Assert(mainDisplay != 0, "CGMainDisplayID returned 0.");

        int showResult = NativeMouseCaptureInterop.CGDisplayShowCursor(mainDisplay);
        Debug.Assert(showResult == CgErrorSuccess, $"CGDisplayShowCursor failed: {showResult}.");

        int associateResult = NativeMouseCaptureInterop.CGAssociateMouseAndMouseCursorPosition(true);
        Debug.Assert(associateResult == CgErrorSuccess,
            $"CGAssociateMouseAndMouseCursorPosition(true) failed: {associateResult}.");

        _isCaptured = false;
        return showResult == CgErrorSuccess && associateResult == CgErrorSuccess;
    }

    public static void Cleanup() {
        if (_isCaptured) {
            DisableCapture();
        }

        _isCaptured = false;
    }
}
