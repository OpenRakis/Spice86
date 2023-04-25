using Microsoft.Extensions.DependencyInjection;

using Serilog.Events;

using Spice86.Core.CLI;
using Spice86.DependencyInjection;
using Spice86.Logging;
using Spice86.Shared.Interfaces;

namespace Spice86;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Microsoft.Win32;
using Spice86.ViewModels;

using System;
using System.Runtime.Versioning;
using Spice86.Views;

internal partial class App : Application {
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string RegistryValueName = "AppsUseLightTheme";

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (!IsInDarkMode()) {
            Styles.RemoveAt(1);
        }

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new PlatformNotSupportedException("Spice86 needs the desktop Linux/Mac/Windows platform in order to run.");
        }

        ServiceProvider serviceProvider = Startup.StartupInjectedServices(desktop.Args);
        ILoggerService? loggerService = serviceProvider.GetService<ILoggerService>();
        if (loggerService is null) {
            throw new InvalidOperationException("Could not get logging service from DI !");
        }

        desktop.MainWindow = new MainWindow();
        MainWindowViewModel mainViewModel = new MainWindowViewModel(loggerService);
        desktop.MainWindow.DataContext = mainViewModel;
        mainViewModel.SetConfiguration(desktop.Args);
        desktop.MainWindow.Closed += (s, e) => mainViewModel.Dispose();
        desktop.MainWindow.Opened += mainViewModel.OnMainWindowOpened;
        base.OnFrameworkInitializationCompleted();
    }

    

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

    private static bool IsInDarkMode() {
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
        try {
            if (OperatingSystem.IsWindows()) {
                return GetIsWindowsInDarkMode();
            }
        } catch {
            //No OS support for themes. Not worth crashing for. Not worth reporting.
        }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        return true;
    }
}