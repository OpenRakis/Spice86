namespace Spice86;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.Views;
using Spice86.Core.CLI;

/// <summary>
/// The main entry point for the Spice86 UI.
/// </summary>
internal partial class App : Application {

    /// <summary>
    /// Initializes the Spice86 UI.
    /// </summary>
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public void SetupMainWindow(Configuration configuration, ILoggerService loggerService) {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) {
            throw new PlatformNotSupportedException("Spice86 needs the desktop Linux/Mac/Windows platform in order to run.");
        }

        desktop.MainWindow = new MainWindow();
        MainWindowViewModel mainViewModel = new(configuration, loggerService);
        desktop.MainWindow.DataContext = mainViewModel;
        desktop.MainWindow.Closed += (_, _) => mainViewModel.Dispose();
    }
}