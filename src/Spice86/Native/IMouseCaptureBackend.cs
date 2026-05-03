namespace Spice86.Native;

using System;

/// <summary>
/// Abstraction over platform-specific mouse capture strategies.
/// </summary>
internal interface IMouseCaptureBackend : IDisposable {
    /// <summary>
    /// Gets a value indicating whether mouse capture is currently active.
    /// </summary>
    bool IsCaptured { get; }

    /// <summary>
    /// Gets a value indicating whether this backend hides the cursor and reports only relative
    /// mouse deltas (e.g. SDL relative mode). When <see langword="false"/>, the cursor remains
    /// visible and the host Avalonia pointer events continue to carry absolute coordinates.
    /// </summary>
    bool UsesRelativeMouseMode { get; }

    /// <summary>
    /// Performs any one-time platform initialisation required before
    /// <see cref="EnableCapture"/> or <see cref="DisableCapture"/> may be called.
    /// </summary>
    /// <param name="nativeWindowHandle">
    /// The platform-native window handle (HWND on Windows, X11 Window ID on Linux, NSWindow* on macOS).
    /// </param>
    /// <returns><see langword="true"/> if initialisation succeeded; otherwise <see langword="false"/>.</returns>
    bool TryInitialize(nint nativeWindowHandle);

    /// <summary>Enables mouse capture.</summary>
    /// <returns><see langword="true"/> if capture was successfully enabled.</returns>
    bool EnableCapture();

    /// <summary>Disables mouse capture.</summary>
    /// <returns><see langword="true"/> if capture was successfully disabled.</returns>
    bool DisableCapture();

    /// <summary>
    /// Returns the accumulated relative mouse motion since the last call.
    /// Always returns <c>(0, 0)</c> for backends where <see cref="UsesRelativeMouseMode"/> is
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="dx">Horizontal delta in pixels (positive = right).</param>
    /// <param name="dy">Vertical delta in pixels (positive = down).</param>
    void GetRelativeMouseDelta(out int dx, out int dy);
}
