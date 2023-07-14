namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.ViewModels;

public partial class DebugWindow : Window {
    public DebugWindow() => InitializeComponent();

    public DebugWindow(IVideoState videoState, IVgaRenderer renderer) {
        InitializeComponent();
        if (!Design.IsDesignMode &&
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            Owner = desktop.MainWindow;
        }

        DataContext = new DebugViewModel(videoState, renderer);
    }
}