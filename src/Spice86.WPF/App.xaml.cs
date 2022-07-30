namespace Spice86.WPF;

using Spice86.UI.ViewModels;
using Spice86.WPF.Views;

using System.Windows;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
    private void Application_Startup(object sender, StartupEventArgs e) {
        var mainViewModel = new WPFMainWindowViewModel();
        mainViewModel.SetConfiguration(e.Args);
        var mainWindow = new MainWindow() {
            DataContext = mainViewModel
        };
        mainWindow.Closed += (s, e) => mainViewModel.Dispose();

        mainWindow.Initialized += mainViewModel.OnMainWindowOpened;
        Application.Current.MainWindow = mainWindow;
        
        mainWindow.Show();

    }
}
