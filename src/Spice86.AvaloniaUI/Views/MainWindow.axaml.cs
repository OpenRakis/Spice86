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
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private Image? _primaryDisplay = null;

    public void SetPrimaryDisplayControl(Image image) {
        if(_primaryDisplay != image) {
            _primaryDisplay = image;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        if (DataContext is MainWindowViewModel vm) {
            vm.OnKeyUp(e);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if(_primaryDisplay is not null) {
            FocusManager.Instance?.Focus(_primaryDisplay);
        }
        if (DataContext is MainWindowViewModel vm) {
            vm.OnKeyDown(e);
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