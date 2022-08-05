namespace Spice86.WPF.Views;
using System.Windows;

/// <summary>
/// Interaction logic for PerformanceWindow.xaml
/// </summary>
public partial class PerformanceWindow : Window {
    public PerformanceWindow() {
        InitializeComponent();
        Owner = App.Current.MainWindow;
    }
}
