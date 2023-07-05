namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Spice86.ViewModels;

internal partial class PaletteWindow : Window {

    public PaletteWindow() {
        InitializeComponent();
    }

    public PaletteWindow(PaletteViewModel paletteViewModel) {
        InitializeComponent();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            Owner = desktop.MainWindow;
        }

        DataContext = paletteViewModel;
    }
}
