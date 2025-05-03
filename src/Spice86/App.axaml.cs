namespace Spice86;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using Spice86.Core.CLI;
using Spice86.ViewModels;
using Spice86.Views;

using System;
using System.ComponentModel;

/// <summary>
/// The main entry point for the Spice86 UI.
/// </summary>
internal partial class App : Application {
    /// <summary>
    /// Initializes the Spice86 UI.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            // This is the main window of the application.
            MainWindow mainWindow = new();
            desktop.MainWindow = mainWindow;
            mainWindow.IsEnabled = false;
            mainWindow.DataContext = this;
            StatusMessage = "Loading...";
            mainWindow.Loaded += (_, _) => OnMainWindowLoaded(mainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnMainWindowLoaded(MainWindow mainWindow) {
        Configuration configuration = new CommandLineParser().ParseCommandLine(
            Environment.GetCommandLineArgs())!;
        Spice86DependencyInjection dependencyInjection = new(configuration, mainWindow);
        if (mainWindow.DataContext is MainWindowViewModel mainVm) {
            mainWindow.IsEnabled = true;
            mainVm.CloseMainWindow += (_, _) => mainWindow.Close();
            mainVm.InvalidateBitmap += mainWindow.Image.InvalidateVisual;
            mainWindow.Image.PointerMoved += (s, e) => mainVm.OnMouseMoved(e, mainWindow.Image);
            mainWindow.Image.PointerPressed += (s, e) => mainVm.OnMouseButtonDown(e, mainWindow.Image);
            mainWindow.Image.PointerReleased += (s, e) => mainVm.OnMouseButtonUp(e, mainWindow.Image);
            mainVm.StartEmulator();
        }
    }

    public static readonly StyledProperty<string> StatusMessageProperty =
        AvaloniaProperty.Register<App, string>(nameof(StatusMessage), defaultValue: "Loading...");

    public string StatusMessage {
        get => GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }
}