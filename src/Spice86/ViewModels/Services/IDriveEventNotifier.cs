namespace Spice86.ViewModels.Services;

/// <summary>
/// Presents toast notifications for important drive events (mount, unmount, disc swap, and errors).
/// Implementations may be disabled (no-op) or active (Avalonia <c>WindowNotificationManager</c>).
/// </summary>
public interface IDriveEventNotifier {
    /// <summary>Shows an informational notification about a drive event.</summary>
    /// <param name="title">The notification title (e.g. "Drive A: mounted").</param>
    /// <param name="message">The detail message (e.g. image path or folder).</param>
    void Notify(string title, string message);

    /// <summary>Shows an error notification for a failed drive operation.</summary>
    /// <param name="title">The notification title (e.g. "Drive D: mount failed").</param>
    /// <param name="message">The error detail (e.g. the exception message or reason).</param>
    void NotifyError(string title, string message);
}
