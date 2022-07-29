namespace Spice86.WPF;

using Spice86.UI.ViewModels;

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
    private void Application_Startup(object sender, StartupEventArgs e) {
        var mainWindow = new MainWindow() {
            DataContext = new WPFMainWindowViewModel()
        };
    }
}
