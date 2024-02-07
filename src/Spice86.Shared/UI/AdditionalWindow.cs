namespace Spice86.Shared.UI;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

/// <summary>
/// The static class for additional windows that can be added to the UI by user code.
/// </summary>
public static class AdditionalWindow {
    /// <summary>
    /// Brings the window to the forefront.
    /// </summary>
    public static void Show(WindowBase window) {
        Dispatcher.UIThread.Post(() => {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is null) {
                return;
            }
            if (desktop.Windows.Any(x => x == window)) {
                window.Activate();
            } else {
                window.Show();
            }
        });
    }
}