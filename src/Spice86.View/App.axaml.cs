namespace Spice86.View;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Live.Avalonia;

using Microsoft.Win32;

using ReactiveUI;

using Spice86.View.ViewModels;
using Spice86.View.Views;

using System;
using System.Diagnostics;
using System.Reactive;
using System.Runtime.Versioning;

public class App : Application, ILiveView {
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string RegistryValueName = "AppsUseLightTheme";

    // When any of the source files change, a new version of the assembly is built, and this
    // method gets called. The returned content gets embedded into the LiveViewHost window.
    public object CreateView(Window window) => new MainWindow();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            if (IsInDarkMode() == false) {
                this.Styles.RemoveAt(1);
            }

            if (Debugger.IsAttached || IsProduction()) {
                // Debugging requires pdb loading etc, so we disable live reloading during a
                // test run with an attached debugger.
                var mainWindow = new MainWindow();
                mainWindow.DataContext = MainWindowViewModel.Create(mainWindow);
                mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
            } else {
                // Here, we create a new LiveViewHost, located in the 'Live.Avalonia' namespace,
                // and pass an ILiveView implementation to it. The ILiveView implementation
                // should have a parameterless constructor! Next, we start listening for any
                // changes in the source files. And then, we show the LiveViewHost window.
                // Simple enough, huh?
                var window = new LiveViewHost(this, Console.WriteLine);
                window.StartWatchingSourceFilesForHotReloading();
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                window.Show();
            }

            // Here we subscribe to ReactiveUI default exception handler to avoid app
            // termination in case if we do something wrong in our view models. See: https://www.reactiveui.net/docs/handbook/default-exception-handler/
            //
            // In case if you are using another MV* framework, please refer to its documentation
            // explaining global exception handling.
            RxApp.DefaultExceptionHandler = Observer.Create<Exception>(Console.WriteLine);

            base.OnFrameworkInitializationCompleted();
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool GetIsWindowsInDarkMode() {
        var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
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