namespace Spice86.Infrastructure;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using Spice86.ViewModels;
using Spice86.Views;

/// <inheritdoc cref="IWindowService"/>
public class WindowService : IWindowService {
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