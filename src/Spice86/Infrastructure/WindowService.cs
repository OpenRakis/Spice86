namespace Spice86.Infrastructure;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using Spice86.ViewModels;
using Spice86.Views;

/// <inheritdoc cref="IWindowService"/>
public class WindowService : IWindowService {
    public void CloseDebugWindow() {
        Dispatcher.UIThread.Post(() => {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime) {
                return;
            }
            DebugWindow? debugWindow = lifetime.Windows.FirstOrDefault(x => x is DebugWindow) as DebugWindow;
            debugWindow?.Close();
        });
    }
    
    public async Task ShowDebugWindow(DebugWindowViewModel viewModel) {
        await Dispatcher.UIThread.InvokeAsync(() => {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime) {
                return;
            }

            bool foundWindow = false;
            IReadOnlyList<WindowBase> windows = lifetime.Windows;
            foreach (WindowBase window in windows) {
                if (window.DataContext?.GetType() != viewModel?.GetType()) {
                    continue;
                }
                foundWindow = true;
                window.Activate();
                break;
            }

            if (foundWindow) {
                return;
            }

            WindowBase debugWindow = new DebugWindow {
                DataContext = viewModel
            };
            debugWindow.Show();
        });
    }
}

/// <summary>
/// Service used for showing the debug window.
/// </summary>
public interface IWindowService {
    /// <summary>
    /// Shows the debug window.
    /// </summary>
    /// <param name="viewModel">The <see cref="DebugWindowViewModel"/> used as DataContext in case the window needs to be created.</param>
    Task ShowDebugWindow(DebugWindowViewModel viewModel);
    
    /// <summary>
    /// Close the debug window.
    /// </summary>
    void CloseDebugWindow();
}