namespace Spice86.WPF;

using Microsoft.Win32;

using Spice86.UI.ViewModels;
using Spice86.WPF.Views;

using System.Runtime.Versioning;
using System.Windows;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string RegistryValueName = "AppsUseLightTheme";

    private void Application_Startup(object sender, StartupEventArgs e) {
        var mainViewModel = new WPFMainWindowViewModel();
        mainViewModel.SetConfiguration(e.Args);
        var mainWindow = new MainWindow() {
            DataContext = mainViewModel
        };
        mainWindow.Closing += (s, e) => mainViewModel.Dispose();
        Application.Current.MainWindow = mainWindow;
        mainWindow.Show();
        mainViewModel.OnMainWindowOpened(this, EventArgs.Empty);
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
        return false;
    }
}
