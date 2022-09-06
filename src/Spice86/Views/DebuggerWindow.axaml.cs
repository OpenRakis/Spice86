namespace Spice86.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

internal partial class DebuggerWindow : Window {
    public DebuggerWindow() {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        if (!Design.IsDesignMode &&
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            Owner = desktop.MainWindow;
        }
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}
