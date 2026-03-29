namespace Spice86.Views.Behaviors;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

using Spice86.ViewModels;
using Spice86.Views;

using System.Diagnostics.CodeAnalysis;

internal class ShowAdditionnalWindowBehavior : Behavior<Control> {
    protected override void OnAttached() {
        base.OnAttached();
        if (AssociatedObject is null) {
            return;
        }
        if (AssociatedObject is MenuItem menuItem) {
            menuItem.Click += OnMenuItemClick;
            return;
        }

        AssociatedObject.PointerPressed += OnPointerPressed;
    }

    protected override void OnDetaching() {
        base.OnDetaching();
        if (AssociatedObject is null) {
            return;
        }

        if (AssociatedObject is MenuItem menuItem) {
            menuItem.Click -= OnMenuItemClick;
            return;
        }

        AssociatedObject.PointerPressed -= OnPointerPressed;
    }

    private void ShowRegisteredWindow<T>(ref object? dataContext, string name) where T : Window, new() {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime) {
            throw new PlatformNotSupportedException("This behavior is only supported on Desktop platforms.");
        }
        IReadOnlyList<Window>? ownedWindows = lifetime.MainWindow!.OwnedWindows;
        if (TryShowRegisteredWindow(ownedWindows, out T? ownedWindow)) {
            dataContext = ownedWindow.DataContext;
            return;
        }
        IReadOnlyList<Window> appWindows = lifetime.Windows;
        if (TryShowRegisteredWindow(appWindows, out T? appWindow)) {
            dataContext = appWindow.DataContext;
        } else {
            dataContext ??= Application.Current.Resources[name];
            T window = new() {
                DataContext = dataContext
            };
            dataContext = window.DataContext;
            window.Show();
        }
    }

    private object? _audioMixerDataContext;

    internal void ShowAudioMixer() {
        ShowRegisteredWindow<MixerView>(ref _audioMixerDataContext, nameof(SoftwareMixerViewModel));
    }

    private object? _debugWindowDataContext;

    /// <summary>
    /// Command implementation for buttons, for which an attached behavior doesn't work.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">The platform is not <see cref="IClassicDesktopStyleApplicationLifetime"/></exception>
    internal void ShowInternalDebugger() {
        ShowRegisteredWindow<DebugWindow>(ref _debugWindowDataContext, nameof(DebugWindowViewModel));
    }

    private object? _httpApiDataContext;

    internal void ShowHttpApi() {
        ShowRegisteredWindow<HttpApiWindow>(ref _httpApiDataContext, nameof(HttpApiViewModel));
    }

    private object? _mcpToolsDataContext;

    internal void ShowMcpTools() {
        ShowRegisteredWindow<McpToolsView>(ref _mcpToolsDataContext, nameof(McpStatusViewModel));
    }

    private object? _joystickPanelDataContext;

    internal void ShowJoystickPanel() {
        ShowRegisteredWindow<JoystickPanelView>(ref _joystickPanelDataContext, nameof(JoystickPanelViewModel));
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
        ShowInternalDebugger();
    }

    private void OnMenuItemClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        // Defer window creation until menu popups are fully closed.
        Dispatcher.UIThread.Post(ShowInternalDebugger, DispatcherPriority.Background);
    }

    private static bool TryShowRegisteredWindow<T>(IReadOnlyList<Window> windows, [NotNullWhen(true)] out T? debugWindow) where T : Window {
        foreach (Window window in windows) {
            if (window is not T registeredWindow) {
                continue;
            }
            debugWindow = registeredWindow;
            if (debugWindow.IsVisible) {
                debugWindow.Activate();
            }
            debugWindow.Show();
            return true;
        }
        debugWindow = null;
        return false;
    }
}