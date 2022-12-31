namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using Spice86.ViewModels;

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
        FocusOnPrimaryVideoBuffer();
        if (DataContext is MainWindowViewModel vm) {
            vm.OnKeyUp(e);
        }
    }

    private void FocusOnPrimaryVideoBuffer() {
        if (_primaryDisplay is not null) {
            FocusManager.Instance?.Focus(_primaryDisplay);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        FocusOnPrimaryVideoBuffer();
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