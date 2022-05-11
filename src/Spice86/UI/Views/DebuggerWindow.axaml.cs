namespace Spice86.UI.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

public partial class DebuggerWindow : Window {
    public DebuggerWindow() {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        if (!Design.IsDesignMode &&
            App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            this.Owner = desktop.MainWindow;
        }
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}
