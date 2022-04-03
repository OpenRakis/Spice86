namespace Spice86.UI.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Spice86.UI.ViewModels;

using System;
using System.ComponentModel;

public partial class MainWindow : Window {

    public MainWindow() {
        InitializeComponent();
        this.Closing += MainWindow_Closing;
        this.DataContextChanged += MainWindow_DataContextChanged;
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

    private async void MainWindow_DataContextChanged(object? sender, EventArgs e) {
        if (sender is MainWindowViewModel vm) {
            await Dispatcher.UIThread.InvokeAsync(() => vm.SetResolution(320, 200, 1), DispatcherPriority.MaxValue);

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