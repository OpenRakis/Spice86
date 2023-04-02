namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.ApplicationLifetimes;

using Spice86.Core.Emulator.VM;
using Spice86.ViewModels;

public partial class DebugWindow : Window {
    public DebugWindow() {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        if (!Design.IsDesignMode &&
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            Owner = desktop.MainWindow;
        }
    }
    
    public DebugWindow(Machine machine) {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        if (!Design.IsDesignMode &&
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            Owner = desktop.MainWindow;
        }

        DataContext = new DebugViewModel(machine);
    }
    
    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }
}