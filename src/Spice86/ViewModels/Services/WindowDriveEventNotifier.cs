namespace Spice86.ViewModels.Services;

using Avalonia.Controls.Notifications;

/// <summary>
/// An <see cref="IDriveEventNotifier"/> that forwards notifications to an Avalonia
/// <see cref="INotificationManager"/> (typically a <see cref="WindowNotificationManager"/>).
/// </summary>
public sealed class WindowDriveEventNotifier : IDriveEventNotifier {
    private readonly INotificationManager _notificationManager;

    /// <summary>
    /// Initialises a new <see cref="WindowDriveEventNotifier"/>.
    /// </summary>
    /// <param name="notificationManager">The Avalonia notification manager to post to.</param>
    public WindowDriveEventNotifier(INotificationManager notificationManager) {
        _notificationManager = notificationManager;
    }

    /// <inheritdoc/>
    public void Notify(string title, string message) {
        _notificationManager.Show(new Notification(title, message, NotificationType.Information));
    }
}
