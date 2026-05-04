namespace Spice86.ViewModels.Services;

/// <summary>
/// A no-op <see cref="IDriveEventNotifier"/> that silently discards all notifications.
/// Used when toast notifications are disabled in the configuration.
/// </summary>
public sealed class NullDriveEventNotifier : IDriveEventNotifier {
    /// <inheritdoc/>
    public void Notify(string title, string message) {
        // Intentionally empty — notifications are disabled.
    }
}
