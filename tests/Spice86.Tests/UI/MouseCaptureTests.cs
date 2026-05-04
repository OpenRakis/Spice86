namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using FluentAssertions;

using Spice86.Native;

using Xunit;

/// <summary>
/// Integration tests for mouse capture backends (<see cref="IMouseCaptureBackend"/>).
/// </summary>
public class MouseCaptureTests {
    // ── SdlMouseCapture ────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="SdlMouseCapture.TryInitialize"/> returns false when given a zero native handle,
    /// so the class never calls into SDL for an invalid window.
    /// </summary>
    [AvaloniaFact]
    public void SdlMouseCapture_TryInitialize_ReturnsFalse_WhenHandleIsZero() {
        // Arrange
        using SdlMouseCapture capture = new();

        // Act
        bool result = capture.TryInitialize(nint.Zero);

        // Assert
        result.Should().BeFalse();
        capture.IsInitialized.Should().BeFalse();
    }

    /// <summary>
    /// <see cref="SdlMouseCapture.EnableCapture"/> returns false and does not set IsCaptured
    /// when SDL has not been initialized (e.g. headless environment or invalid handle).
    /// </summary>
    [AvaloniaFact]
    public void SdlMouseCapture_EnableCapture_ReturnsFalse_WhenNotInitialized() {
        // Arrange
        using SdlMouseCapture capture = new();

        // Act
        bool result = capture.EnableCapture();

        // Assert
        result.Should().BeFalse();
        capture.IsCaptured.Should().BeFalse();
    }

    /// <summary>
    /// <see cref="SdlMouseCapture.DisableCapture"/> returns false when SDL is not initialized.
    /// </summary>
    [AvaloniaFact]
    public void SdlMouseCapture_DisableCapture_ReturnsFalse_WhenNotInitialized() {
        // Arrange
        using SdlMouseCapture capture = new();

        // Act
        bool result = capture.DisableCapture();

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// <see cref="SdlMouseCapture.GetRelativeMouseDelta"/> returns zero deltas when not initialized,
    /// so callers receive a neutral value and do not accumulate phantom motion.
    /// </summary>
    [AvaloniaFact]
    public void SdlMouseCapture_GetRelativeMouseDelta_ReturnsZero_WhenNotInitialized() {
        // Arrange
        using SdlMouseCapture capture = new();

        // Act
        capture.GetRelativeMouseDelta(out int dx, out int dy);

        // Assert
        dx.Should().Be(0);
        dy.Should().Be(0);
    }

    /// <summary>
    /// <see cref="SdlMouseCapture"/> can be disposed multiple times without throwing an exception
    /// (idempotent Dispose pattern).
    /// </summary>
    [AvaloniaFact]
    public void SdlMouseCapture_Dispose_IsIdempotent() {
        // Arrange
        SdlMouseCapture capture = new();

        // Act
        Action doubleDispose = () => {
            capture.Dispose();
            capture.Dispose();
        };

        // Assert
        doubleDispose.Should().NotThrow();
    }

    /// <summary>
    /// <see cref="SdlMouseCapture"/> uses relative mouse mode.
    /// </summary>
    [AvaloniaFact]
    public void SdlMouseCapture_UsesRelativeMouseMode_IsTrue() {
        // Arrange
        using SdlMouseCapture capture = new();

        // Assert
        capture.UsesRelativeMouseMode.Should().BeTrue();
    }

    // ── WindowsMouseCaptureBackend ─────────────────────────────────────────────

    /// <summary>
    /// <see cref="WindowsMouseCaptureBackend.TryInitialize"/> returns false when given a zero native
    /// handle, so no Win32 calls are attempted.
    /// </summary>
    [AvaloniaFact]
    public void WindowsMouseCaptureBackend_TryInitialize_ReturnsFalse_WhenHandleIsZero() {
        // Arrange
        using WindowsMouseCaptureBackend backend = new();

        // Act
        bool result = backend.TryInitialize(nint.Zero);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// <see cref="WindowsMouseCaptureBackend.EnableCapture"/> returns false when not initialized.
    /// </summary>
    [AvaloniaFact]
    public void WindowsMouseCaptureBackend_EnableCapture_ReturnsFalse_WhenNotInitialized() {
        // Arrange
        using WindowsMouseCaptureBackend backend = new();

        // Act
        bool result = backend.EnableCapture();

        // Assert
        result.Should().BeFalse();
        backend.IsCaptured.Should().BeFalse();
    }

    /// <summary>
    /// <see cref="WindowsMouseCaptureBackend.GetRelativeMouseDelta"/> always returns zero
    /// because the Windows backend uses absolute cursor clipping, not relative deltas.
    /// </summary>
    [AvaloniaFact]
    public void WindowsMouseCaptureBackend_GetRelativeMouseDelta_AlwaysReturnsZero() {
        // Arrange
        using WindowsMouseCaptureBackend backend = new();

        // Act
        backend.GetRelativeMouseDelta(out int dx, out int dy);

        // Assert
        dx.Should().Be(0);
        dy.Should().Be(0);
    }

    /// <summary>
    /// <see cref="WindowsMouseCaptureBackend"/> does not use relative mouse mode;
    /// Avalonia pointer events carry absolute coordinates and no delta polling is needed.
    /// </summary>
    [AvaloniaFact]
    public void WindowsMouseCaptureBackend_UsesRelativeMouseMode_IsFalse() {
        // Arrange
        using WindowsMouseCaptureBackend backend = new();

        // Assert
        backend.UsesRelativeMouseMode.Should().BeFalse();
    }

    /// <summary>
    /// <see cref="WindowsMouseCaptureBackend"/> can be disposed multiple times without throwing.
    /// </summary>
    [AvaloniaFact]
    public void WindowsMouseCaptureBackend_Dispose_IsIdempotent() {
        // Arrange
        WindowsMouseCaptureBackend backend = new();

        // Act
        Action doubleDispose = () => {
            backend.Dispose();
            backend.Dispose();
        };

        // Assert
        doubleDispose.Should().NotThrow();
    }

    // ── IMouseCaptureBackend contract ──────────────────────────────────────────

    /// <summary>
    /// After a failed <see cref="IMouseCaptureBackend.TryInitialize"/>, IsCaptured must be false.
    /// </summary>
    [AvaloniaFact]
    public void SdlMouseCapture_StateIsConsistent_AfterFailedInitialize() {
        // Arrange
        using SdlMouseCapture capture = new();

        // Act
        capture.TryInitialize(nint.Zero);

        // Assert
        capture.IsInitialized.Should().BeFalse();
        capture.IsCaptured.Should().BeFalse();
    }
}


