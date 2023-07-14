namespace Spice86.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

internal partial class PerformanceWindow : Window {
    public PerformanceWindow() {
        InitializeComponent();
        if (!Design.IsDesignMode &&
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            Owner = desktop.MainWindow;
        }
    }
}
