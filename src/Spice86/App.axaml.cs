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
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public void SetupMainWindow(IClassicDesktopStyleApplicationLifetime desktop, Configuration configuration, ILoggerService loggerService) {
        desktop.MainWindow = new MainWindow(desktop, configuration, loggerService);
    }
}