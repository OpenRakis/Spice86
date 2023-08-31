namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Spice86.Core.Emulator.CPU;
using Spice86.Infrastructure;
using Spice86.Shared.Diagnostics;
using Spice86.ViewModels;

internal partial class PerformanceWindow : Window {
    public PerformanceWindow() => InitializeComponent();

    public PerformanceWindow(IUIDispatcherTimer _uiDispatcherTimer, State cpuState, PerformanceMeasurer performanceMeasurer) {
        InitializeComponent();
        if (!Design.IsDesignMode &&
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            Owner = desktop.MainWindow;
        }
        DataContext = new PerformanceViewModel(_uiDispatcherTimer, cpuState, performanceMeasurer);
    }
}
