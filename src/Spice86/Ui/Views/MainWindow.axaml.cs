namespace Spice86.UI.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using System;
using System.ComponentModel;

public partial class MainWindow : Window {

    public MainWindow() {
        InitializeComponent();
        this.Closing += MainWindow_Closing;
#if DEBUG
        this.AttachDevTools();
#endif
    }

    public static event EventHandler<CancelEventArgs>? AppClosing;

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) {
        AppClosing?.Invoke(sender, e);
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}