namespace Spice86;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;

using Spice86.Core.CLI;
using Spice86.ViewModels;
using Spice86.Views;

using System;

/// <summary>
/// The main entry point for the Spice86 UI.
/// </summary>
internal partial class App : Application {
    private const string Spice86ControlThemesSource = "avares://Spice86/Assets/ControlThemes.axaml";
    private const string Spice86StylesSource = "avares://Spice86/Styles/Spice86.axaml";

    /// <summary>
    /// Initializes the Spice86 UI.
    /// </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted() {

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            SplashWindow splashWindow = new();
            desktop.MainWindow = splashWindow;
            splashWindow.Loaded += (_, _) => {
                OnSplashWindowLoaded(desktop, splashWindow);
            };
            splashWindow.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// A one-time event handler that enables us to delay-load *some* App resources programmatically.
    /// If possible, it's preferable to do this, rather than including them in App.xaml.<br/>
    /// The latter delays the loading of the app.<br/><br/>
    /// This event-handler also enables us to load the UI first, and then start the emulator here.
    /// </summary>
    /// <remarks>For example, <see cref="Semi" /> theme is in App.xaml and not here. Otherwise the application theme is wrong.</remarks>
    private void OnSplashWindowLoaded(
        IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splashWindow) {
        MainWindow mainWindow = new();
        LoadAppResources();
        Configuration configuration = new CommandLineParser().ParseCommandLine(
                desktop.Args!)!;
        Spice86DependencyInjection dependencyInjection = new(configuration, mainWindow);
        if (mainWindow.DataContext is MainWindowViewModel mainVm) {
            mainVm.CloseMainWindow += (_, _) => mainWindow.Close();
            mainVm.InvalidateBitmap += mainWindow.Image.InvalidateVisual;
            mainWindow.Image.PointerMoved += (s, e) => mainVm.OnMouseMoved(e, mainWindow.Image);
            mainWindow.Image.PointerPressed += (s, e) => mainVm.OnMouseButtonDown(e, mainWindow.Image);
            mainWindow.Image.PointerReleased += (s, e) => mainVm.OnMouseButtonUp(e, mainWindow.Image);
            mainVm.StartEmulator();
            mainVm.Disposing += dependencyInjection.Dispose;
        }
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        splashWindow.Close();
    }

    private static void LoadAppResources() {
        IResourceDictionary appResources = Application.Current!.Resources;

        ResourceInclude controlThemes = new ResourceInclude(new Uri(
            Spice86ControlThemesSource)) {
            Source = new Uri(Spice86ControlThemesSource)
        };
        appResources.MergedDictionaries.Add(controlThemes);

        Avalonia.Styling.Styles appStyles = Application.Current!.Styles;

        appStyles.Add(new StyleInclude(new Uri(
            Spice86StylesSource)) {
            Source = new Uri(Spice86StylesSource)
        });
    }
}