namespace Spice86.Native;

using Silk.NET.SDL;

using System;
using System.IO;

/// <summary>
/// SDL2-based mouse capture backend for non-Windows platforms.
/// Uses <c>SDL_SetRelativeMouseMode</c> to hide the cursor and report relative mouse deltas,
/// analogous to the approach used in dosbox-staging.
/// Precompiled SDL2 native libraries are bundled via Silk.NET.SDL (Ultz.Native.SDL),
/// covering macOS (universal) and Linux (x64/arm64/arm).
/// </summary>
internal sealed class SdlMouseCapture : IMouseCaptureBackend {
    private Sdl? _sdl;
    private bool _initialized;
    private bool _isCaptured;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether the SDL subsystem was successfully initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Gets a value indicating whether relative mouse capture is currently active.
    /// </summary>
    public bool IsCaptured => _isCaptured;

    /// <inheritdoc/>
    /// <remarks><see langword="true"/>: cursor is hidden and only deltas are reported.</remarks>
    public bool UsesRelativeMouseMode => true;

    /// <summary>
    /// Initializes the SDL video subsystem.
    /// If the SDL2 native library is unavailable, this returns <see langword="false"/> without throwing.
    /// </summary>
    /// <param name="nativeWindowHandle">The platform-native window handle (HWND on Windows, X11 Window ID on Linux, NSWindow* on macOS).</param>
    /// <returns><see langword="true"/> if initialization succeeded; otherwise <see langword="false"/>.</returns>
    public bool TryInitialize(nint nativeWindowHandle) {
        if (_initialized) {
            return true;
        }

        if (nativeWindowHandle == nint.Zero) {
            return false;
        }

        try {
            _sdl = Sdl.GetApi();
        } catch (FileNotFoundException) {
            return false;
        } catch (DllNotFoundException) {
            return false;
        }

        // Suppress SDL's default signal handlers so they don't interfere with the host app.
        _sdl.SetHint("SDL_NO_SIGNAL_HANDLERS", "1");

        // Initialize only the video subsystem so SDL can manage mouse state.
        int result = _sdl.Init(Sdl.InitVideo);
        if (result != 0) {
            _sdl.Dispose();
            _sdl = null;
            return false;
        }

        _initialized = true;
        return true;
    }

    /// <summary>
    /// Enables SDL relative mouse mode.
    /// The cursor is hidden and mouse motion is reported as relative deltas only.
    /// </summary>
    /// <returns><see langword="true"/> if relative mode was enabled successfully.</returns>
    public bool EnableCapture() {
        if (!_initialized || _isCaptured || _sdl is null) {
            return false;
        }

        int result = _sdl.SetRelativeMouseMode(SdlBool.True);
        if (result != 0) {
            return false;
        }

        _isCaptured = true;
        return true;
    }

    /// <summary>
    /// Disables SDL relative mouse mode, restoring normal cursor behaviour.
    /// </summary>
    /// <returns><see langword="true"/> if relative mode was disabled successfully.</returns>
    public bool DisableCapture() {
        if (!_initialized || !_isCaptured || _sdl is null) {
            return false;
        }

        int result = _sdl.SetRelativeMouseMode(SdlBool.False);
        if (result != 0) {
            return false;
        }

        _isCaptured = false;
        return true;
    }

    /// <summary>
    /// Retrieves the accumulated relative mouse motion since the last call.
    /// Analogous to SDL_GetRelativeMouseState used in dosbox-staging.
    /// </summary>
    /// <param name="dx">Horizontal delta in screen pixels (positive = right).</param>
    /// <param name="dy">Vertical delta in screen pixels (positive = down).</param>
    public void GetRelativeMouseDelta(out int dx, out int dy) {
        if (!_initialized || _sdl is null) {
            dx = 0;
            dy = 0;
            return;
        }

        // SDL_PumpEvents processes pending OS events so GetRelativeMouseState is up to date.
        _sdl.PumpEvents();

        int x = 0;
        int y = 0;
        unsafe {
            _sdl.GetRelativeMouseState(&x, &y);
        }

        dx = x;
        dy = y;
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;

        if (_sdl is null) {
            return;
        }

        if (_isCaptured) {
            _sdl.SetRelativeMouseMode(SdlBool.False);
            _isCaptured = false;
        }

        if (_initialized) {
            _sdl.QuitSubSystem(Sdl.InitVideo);
            _initialized = false;
        }

        _sdl.Dispose();
        _sdl = null;
    }
}
