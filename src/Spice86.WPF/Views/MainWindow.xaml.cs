namespace Spice86.WPF.Views;
using System;
using System.ComponentModel;
using System.Windows;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
    public static event EventHandler<CancelEventArgs>? AppClosing;
}
