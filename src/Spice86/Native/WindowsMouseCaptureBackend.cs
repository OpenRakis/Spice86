namespace Spice86.Native;

using System;

/// <summary>
/// Windows-specific mouse capture backend. Uses <c>ClipCursor</c> to confine the cursor
/// to the emulator window, and <c>SetCapture</c>/<c>ReleaseCapture</c> to ensure mouse
/// messages continue reaching the window even when the cursor temporarily touches an edge.
/// Avalonia <c>PointerMoved</c> events still fire with absolute window-relative coordinates
/// — no delta polling is required.
/// </summary>
internal sealed class WindowsMouseCaptureBackend : IMouseCaptureBackend {
    private nint _windowHandle;
    private bool _isCaptured;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsCaptured => _isCaptured;

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="false"/>: the cursor remains visible and Avalonia pointer events carry
    /// absolute coordinates. The host window simply clips the cursor to its bounds.
    /// </remarks>
    public bool UsesRelativeMouseMode => false;

    /// <inheritdoc/>
    public bool TryInitialize(nint nativeWindowHandle) {
        if (nativeWindowHandle == nint.Zero) {
            return false;
        }

        _windowHandle = nativeWindowHandle;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// May be called repeatedly (e.g. on window move/resize) to refresh the clip rectangle.
    /// </remarks>
    public bool EnableCapture() {
        if (_windowHandle == nint.Zero) {
            return false;
        }

        bool gotClientRect = WindowsMouseCaptureInterop.GetClientRect(_windowHandle, out WindowsMouseCaptureInterop.ClipRect clientRect);
        if (!gotClientRect) {
            return false;
        }

        WindowsMouseCaptureInterop.WinPoint topLeft = new WindowsMouseCaptureInterop.WinPoint { X = clientRect.Left, Y = clientRect.Top };
        WindowsMouseCaptureInterop.WinPoint bottomRight = new WindowsMouseCaptureInterop.WinPoint { X = clientRect.Right, Y = clientRect.Bottom };

        bool topLeftConverted = WindowsMouseCaptureInterop.ClientToScreen(_windowHandle, ref topLeft);
        if (!topLeftConverted) {
            return false;
        }

        bool bottomRightConverted = WindowsMouseCaptureInterop.ClientToScreen(_windowHandle, ref bottomRight);
        if (!bottomRightConverted) {
            return false;
        }

        WindowsMouseCaptureInterop.ClipRect screenRect = new WindowsMouseCaptureInterop.ClipRect {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = bottomRight.X,
            Bottom = bottomRight.Y
        };

        WindowsMouseCaptureInterop.SetCapture(_windowHandle);
        bool clipResult = WindowsMouseCaptureInterop.ClipCursor(ref screenRect);
        if (clipResult) {
            _isCaptured = true;
        }

        return clipResult;
    }

    /// <inheritdoc/>
    public bool DisableCapture() {
        WindowsMouseCaptureInterop.ReleaseCapture();
        bool clipResult = WindowsMouseCaptureInterop.ClipCursor(IntPtr.Zero);
        _isCaptured = false;
        return clipResult;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Always returns <c>(0, 0)</c>: absolute coordinates come from Avalonia pointer events.
    /// </remarks>
    public void GetRelativeMouseDelta(out int dx, out int dy) {
        dx = 0;
        dy = 0;
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;

        if (_isCaptured) {
            DisableCapture();
        }
    }
}
