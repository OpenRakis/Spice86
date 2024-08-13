namespace Spice86.Behaviors;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Xaml.Interactivity;

using Spice86.Views;

using System.Diagnostics.CodeAnalysis;

internal class ShowInternalDebuggerBehavior : Behavior<Control> {
    protected override void OnAttached() {
        base.OnAttached();
        if (AssociatedObject is null) {
            return;
        }
        AssociatedObject.PointerPressed += OnPointerPressed;
    }

    protected override void OnDetaching() {
        base.OnDetaching();
        if (AssociatedObject is null) {
            return;
        }
        AssociatedObject.PointerPressed -= OnPointerPressed;
    }
    
    private object? _debugWindowDataContext;

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime) {
            throw new PlatformNotSupportedException("This behavior is only supported on Desktop platforms.");
        }
        IReadOnlyList<Window>? ownedWindows = lifetime.MainWindow!.OwnedWindows;
        if (TryShowDebugWindow(ownedWindows, out DebugWindow? ownedDebugWindow)) {
            _debugWindowDataContext = ownedDebugWindow.DataContext;
            return;
        }
        IReadOnlyList<Window> appWindows = lifetime.Windows;
        if (TryShowDebugWindow(appWindows, out DebugWindow? appDebugWindow)) {
            _debugWindowDataContext = appDebugWindow.DataContext;
        } else {
            DebugWindow debugWindow = new() {
                DataContext = _debugWindowDataContext
            };
            _debugWindowDataContext = debugWindow.DataContext;
            debugWindow.Show();
        }
    }

    private static bool TryShowDebugWindow(IReadOnlyList<Window> windows, [NotNullWhen(true)] out DebugWindow? debugWindow) {
        foreach (Window window in windows) {
            if (window is not DebugWindow dbgWindow) {
                continue;
            }
            debugWindow = dbgWindow;
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