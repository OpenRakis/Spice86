namespace Spice86.WPF;

using Microsoft.Win32;

using Serilog;

using Spice86.Logging;
using Spice86.UI.ViewModels;
using Spice86.WPF.Views;

using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Windows;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string RegistryValueName = "AppsUseLightTheme";

    private Uri? _currentTheme;

    private static readonly ILogger _logger = new Serilogger().Logger.ForContext<App>();

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        var mainViewModel = new WPFMainWindowViewModel();
        mainViewModel.SetConfiguration(e.Args);
        var mainWindow = new MainWindow() {
            DataContext = mainViewModel
        };
        Application.Current.MainWindow = mainWindow;
        mainWindow.Show();
        mainViewModel.OnMainWindowOpened(this, EventArgs.Empty);
        try {
            WatchTheme();
            CHangeThemeIfWindowsChangedIt();
        } catch {
            //No OS support for themes. Not worth crashing for.
        }
        mainWindow.Closing += OnAppExit;
    }

    private void OnAppExit(object? sender, CancelEventArgs e) {
        ((WPFMainWindowViewModel)App.Current.MainWindow.DataContext).Dispose();
        e.Cancel = false;
    }

    private static void ChangeTheme(Uri theme) {
        if (theme == ResourceLocator.DarkColorScheme) {
            ResourceLocator.SetColorScheme(Current.Resources, ResourceLocator.DarkColorScheme, ResourceLocator.LightColorScheme);
        } else {
            ResourceLocator.SetColorScheme(Current.Resources, ResourceLocator.LightColorScheme, ResourceLocator.DarkColorScheme);
        }
    }

    private static Uri GetWindowsTheme() {
        var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        var registryValueObject = key?.GetValue(RegistryValueName);
        if (registryValueObject == null) {
            return ResourceLocator.LightColorScheme;
        }

        var registryValue = (int)registryValueObject;

        return registryValue > 0 ? ResourceLocator.LightColorScheme : ResourceLocator.DarkColorScheme;
    }

    private void CHangeThemeIfWindowsChangedIt() {
        var newWindowsTheme = GetWindowsTheme();
        if (_currentTheme != newWindowsTheme) {
            _currentTheme = newWindowsTheme;
            ChangeTheme(_currentTheme);
        }
    }

    private void OnWpfUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
        _logger.Error(e.Exception.GetBaseException(), "An unhandled exception occured");
        MessageBox.Show(e.Exception.GetBaseException().GetType().ToString() + Environment.NewLine + e.Exception.GetBaseException().Message, "Une erreur est survenue. Opération annulée.");
        e.Handled = true;
    }

    private void Watcher_EventArrived(object sender, EventArrivedEventArgs e) {
        CHangeThemeIfWindowsChangedIt();
    }

    private void WatchTheme() {
        var currentUser = WindowsIdentity.GetCurrent();
        var query = string.Format(
            CultureInfo.InvariantCulture,
            @"SELECT * FROM RegistryValueChangeEvent WHERE Hive = 'HKEY_USERS' AND KeyPath = '{0}\\{1}' AND ValueName = '{2}'",
            currentUser?.User?.Value,
            RegistryKeyPath.Replace(@"\", @"\\", System.StringComparison.InvariantCulture),
            RegistryValueName);

        using var watcher = new ManagementEventWatcher(query);
        watcher.EventArrived += Watcher_EventArrived;
        watcher.Start();
    }

    private static class ResourceLocator {
        public static Uri DarkColorScheme => new Uri("pack://application:,,,/AdonisUI;component/ColorSchemes/Dark.xaml", UriKind.Absolute);

        public static Uri LightColorScheme => new Uri("pack://application:,,,/AdonisUI;component/ColorSchemes/Light.xaml", UriKind.Absolute);

        public static Uri ClassicTheme => new Uri("pack://application:,,,/AdonisUI.ClassicTheme;component/Resources.xaml", UriKind.Absolute);

        /// <summary>
        /// Adds any Adonis theme to the provided resource dictionary.
        /// </summary>
        /// <param name="rootResourceDictionary">
        /// The resource dictionary containing AdonisUI's resources. Expected are the resource
        /// dictionaries of the app or window.
        /// </param>
        public static void AddAdonisResources(ResourceDictionary rootResourceDictionary) {
            rootResourceDictionary.MergedDictionaries.Add(new ResourceDictionary { Source = ClassicTheme });
        }

        /// <summary>
        /// Removes all resources of AdonisUI from the provided resource dictionary.
        /// </summary>
        /// <param name="rootResourceDictionary">
        /// The resource dictionary containing AdonisUI's resources. Expected are the resource
        /// dictionaries of the app or window.
        /// </param>
        public static void RemoveAdonisResources(ResourceDictionary rootResourceDictionary) {
            Uri[] adonisResources = { ClassicTheme };
            var currentTheme = FindFirstContainedResourceDictionaryByUri(rootResourceDictionary, adonisResources);

            if (currentTheme != null) {
                RemoveResourceDictionaryFromResourcesDeep(currentTheme, rootResourceDictionary);
            }
        }

        /// <summary>
        /// Adds a resource dictionary with the specified uri to the MergedDictionaries
        /// collection of the <see cref="rootResourceDictionary" />. Additionally all child
        /// ResourceDictionaries are traversed recursively to find the current color scheme
        /// which is removed if found.
        /// </summary>
        /// <param name="rootResourceDictionary">
        /// The resource dictionary containing the currently active color scheme. It will
        /// receive the new color scheme in its MergedDictionaries. Expected are the resource
        /// dictionaries of the app or window.
        /// </param>
        /// <param name="colorSchemeResourceUri">
        /// The Uri of the color scheme to be set. Can be taken from the
        /// <see cref="ResourceLocator" /> class.
        /// </param>
        /// <param name="currentColorSchemeResourceUri">
        /// Optional uri to an external color scheme that is not provided by AdonisUI.
        /// </param>
        public static void SetColorScheme(ResourceDictionary rootResourceDictionary, Uri colorSchemeResourceUri, Uri? currentColorSchemeResourceUri = null) {
            var knownColorSchemes = currentColorSchemeResourceUri != null ? new[] { currentColorSchemeResourceUri } : new[] { LightColorScheme, DarkColorScheme };

            var currentTheme = FindFirstContainedResourceDictionaryByUri(rootResourceDictionary, knownColorSchemes);

            if (currentTheme != null) {
                RemoveResourceDictionaryFromResourcesDeep(currentTheme, rootResourceDictionary);
            }

            rootResourceDictionary.MergedDictionaries.Add(new ResourceDictionary { Source = colorSchemeResourceUri });
        }

        private static ResourceDictionary? FindFirstContainedResourceDictionaryByUri(ResourceDictionary resourceDictionary, Uri[] knownColorSchemes) {
            if (knownColorSchemes.Any(scheme => resourceDictionary.Source != null && resourceDictionary.Source.IsAbsoluteUri && resourceDictionary.Source.AbsoluteUri.Equals(scheme.AbsoluteUri, StringComparison.InvariantCulture))) {
                return resourceDictionary;
            }

            if (!resourceDictionary.MergedDictionaries.Any()) {
                return null;
            }

            return resourceDictionary.MergedDictionaries.FirstOrDefault(d => FindFirstContainedResourceDictionaryByUri(d, knownColorSchemes) != null);
        }

        private static bool RemoveResourceDictionaryFromResourcesDeep(ResourceDictionary resourceDictionaryToRemove, ResourceDictionary rootResourceDictionary) {
            if (!rootResourceDictionary.MergedDictionaries.Any()) {
                return false;
            }

            if (rootResourceDictionary.MergedDictionaries.Contains(resourceDictionaryToRemove)) {
                rootResourceDictionary.MergedDictionaries.Remove(resourceDictionaryToRemove);
                return true;
            }

            return rootResourceDictionary.MergedDictionaries.Any(dict => RemoveResourceDictionaryFromResourcesDeep(resourceDictionaryToRemove, dict));
        }
    }
}
