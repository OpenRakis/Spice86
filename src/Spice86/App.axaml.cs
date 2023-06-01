namespace Spice86;

using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Spice86.Shared.Interfaces;
using Spice86.ViewModels;
using Spice86.Views;

/// <summary>
/// The main entry point for the Spice86 UI.
/// </summary>
internal partial class App : Application {
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";

    /// <summary>
    /// Initializes the Spice86 UI.
    /// </summary>
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Called when the framework initialization is completed.
    /// </summary>
    public override void OnFrameworkInitializationCompleted() {
        if (!IsInDarkMode() && Styles.Count > 1) {
            Styles.RemoveAt(1);
        }

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) {
            throw new PlatformNotSupportedException("Spice86 needs the desktop Linux/Mac/Windows platform in order to run.");
        }

        ServiceProvider serviceProvider = Startup.StartupInjectedServices(desktop.Args ?? Array.Empty<string>());

        ILoggerService? loggerService = serviceProvider.GetService<ILoggerService>();
        if (loggerService is null) {
            throw new InvalidOperationException("Could not get logging service from DI !");
        }

        desktop.MainWindow = new MainWindow();
        MainWindowViewModel mainViewModel = new(loggerService);
        desktop.MainWindow.DataContext = mainViewModel;
        mainViewModel.SetConfiguration(desktop.Args ?? Array.Empty<string>());
        desktop.MainWindow.Closed += (_, _) => mainViewModel.Dispose();
        desktop.MainWindow.Opened += mainViewModel.OnMainWindowOpened;
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Checks whether the Windows OS is in dark mode.
    /// </summary>
    /// <returns>True if the Windows OS is in dark mode, False otherwise.</returns>
    [SupportedOSPlatform("windows")]
    private static bool GetIsWindowsInDarkMode() {
        RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        object? registryValueObject = key?.GetValue(RegistryValueName);
        if (registryValueObject == null) {
            return false;
        }

        int registryValue = (int)registryValueObject;
        return registryValue <= 0;
    }

    /// <summary>
    /// Checks whether the current OS is in dark mode.
    /// </summary>
    /// <returns>True if the current OS is in dark mode, False otherwise.</returns>
    private static bool IsInDarkMode() {
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
        try {
            if (OperatingSystem.IsWindows()) {
                return GetIsWindowsInDarkMode();
            }
        }
        catch {
            //No OS support for themes. Not worth crashing for. Not worth reporting.
        }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        return true;
    }
}