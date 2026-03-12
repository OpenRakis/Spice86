using System;
using System.Runtime.InteropServices;

namespace Spice86.Native {
    internal static class NativeMouseCapture {
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly bool IsMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        private static bool _isCaptured;

        public static bool EnableCapture(IntPtr windowHandle) {
            if (windowHandle == IntPtr.Zero) {
                return false;
            }

            if (IsWindows) {
                _isCaptured = WindowsMouseCaptureBackend.EnableCapture(windowHandle);
                return _isCaptured;
            }

            if (IsMacOs) {
                _isCaptured = MacOsMouseCaptureBackend.EnableCapture();
                return _isCaptured;
            }

            if (IsLinux) {
                _isCaptured = X11MouseCaptureBackend.EnableCapture(windowHandle);
                return _isCaptured;
            }

            return false;
        }

        public static bool DisableCapture() {
            if (IsWindows) {
                bool result = WindowsMouseCaptureBackend.DisableCapture();
                if (result) {
                    _isCaptured = false;
                }

                return result;
            }

            if (IsMacOs) {
                bool result = MacOsMouseCaptureBackend.DisableCapture();
                if (result) {
                    _isCaptured = false;
                }

                return result;
            }

            if (IsLinux) {
                bool success = X11MouseCaptureBackend.DisableCapture();
                if (success) {
                    _isCaptured = false;
                }

                return success;
            }

            return false;
        }

        public static void Cleanup() {
            if (_isCaptured) {
                DisableCapture();
            }

            if (IsLinux) {
                X11MouseCaptureBackend.Cleanup();
            }

            if (IsWindows) {
                WindowsMouseCaptureBackend.Cleanup();
            }

            if (IsMacOs) {
                MacOsMouseCaptureBackend.Cleanup();
            }

            _isCaptured = false;
        }

        public static bool IsCaptured => _isCaptured;
    }
}