namespace Spice86;

using Microsoft.Extensions.DependencyInjection;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Spice86.Core.CLI;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.Views;

/// <summary>
/// The entry point for the Spice86 UI.
/// </summary>
internal partial class App : Application {
    /// <summary>
    /// Initializes the Spice86 UI.
    /// </summary>
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public void SetupMainWindow(IServiceProvider serviceProvider) {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) {
            throw new PlatformNotSupportedException("Spice86 needs the desktop Linux/Mac/Windows platform in order to run.");
        }

        ILoggerService loggerService = serviceProvider.GetRequiredService<ILoggerService>();
        ICommandLineParser commandLineParser = serviceProvider.GetRequiredService<ICommandLineParser>();

        desktop.MainWindow = new MainWindow();
        MainWindowViewModel mainViewModel = new(commandLineParser, loggerService);
        desktop.MainWindow.DataContext = mainViewModel;
        mainViewModel.SetConfiguration(desktop.Args);
        desktop.MainWindow.Closed += (_, _) => mainViewModel.Dispose();
        desktop.MainWindow.Opened += mainViewModel.OnMainWindowOpened;
    }
}