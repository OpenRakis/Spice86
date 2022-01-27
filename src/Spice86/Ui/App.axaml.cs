namespace Spice86.UI;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Microsoft.Win32;

using Spice86.UI.ViewModels;
using Spice86.UI.Views;

using System;
using System.Runtime.Versioning;

using Serilog;
using System.Reactive;
using ReactiveUI;
using Avalonia.Controls.ApplicationLifetimes;

public partial class App : Application {
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string RegistryValueName = "AppsUseLightTheme";

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        if (IsInDarkMode() == false) {
            this.Styles.RemoveAt(1);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var mainViewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow {
                DataContext = mainViewModel,
            };
            desktop.MainWindow.Closed += (s, e) => mainViewModel.Exit();

            desktop.MainWindow.Opened += mainViewModel.OnMainWindowOpened;
        }
        base.OnFrameworkInitializationCompleted();
    }

    [SupportedOSPlatform("windows")]
    private static bool GetIsWindowsInDarkMode() {
        RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        var registryValueObject = key?.GetValue(RegistryValueName);
        if (registryValueObject == null) {
            return false;
        }

        var registryValue = (int)registryValueObject;
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
        return false;
    }

    private static bool IsProduction() {
#if DEBUG
        return false;
#else
            return true;
#endif
    }
}