namespace Spice86.WPF.Views;

using Spice86.UI.ViewModels;

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        base.OnKeyUp(e);
        if (DataContext is WPFMainWindowViewModel vm) {
            vm.OnKeyUp(e);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (DataContext is WPFMainWindowViewModel vm) {
            vm.OnKeyDown(e);
        }
    }


    public static event EventHandler<CancelEventArgs>? AppClosing;
}
