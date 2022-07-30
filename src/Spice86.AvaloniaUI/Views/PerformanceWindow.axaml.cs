namespace Spice86.AvaloniaUI.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

internal partial class PerformanceWindow : Window {
    public PerformanceWindow() {
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
