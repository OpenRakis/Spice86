namespace Spice86.AvaloniaUI.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Spice86.AvaloniaUI.ViewModels;

using System;
using System.ComponentModel;

internal partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
        Closing += MainWindow_Closing;
        DataContextChanged += MainWindow_DataContextChanged;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        base.OnKeyUp(e);
        if (DataContext is MainWindowViewModel vm) {
            vm.OnKeyUp(e);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (DataContext is MainWindowViewModel vm) {
            vm.OnKeyDown(e);
        }
    }

    private void MainWindow_DataContextChanged(object? sender, EventArgs e) {
        if (sender is MainWindowViewModel vm) {
            Dispatcher.UIThread.Post(() => vm.SetResolution(320, 200, 1));
        }
    }

    public static event EventHandler<CancelEventArgs>? AppClosing;

    private void MainWindow_Closing(object? sender, CancelEventArgs e) {
        AppClosing?.Invoke(sender, e);
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}