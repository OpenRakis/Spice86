namespace Spice86.Views;
using Avalonia.Controls;
using Avalonia.Input;

using Spice86.ViewModels;

using System;

public partial class BreakpointsView : UserControl
{
    public BreakpointsView()
    {
        InitializeComponent();
        BreakpointsDataGrid.KeyUp += BreakpointsDataGrid_KeyUp;
    }

    private void BreakpointsDataGrid_KeyUp(object? sender, KeyEventArgs e) {
        if (e.Key == Key.Delete && DataContext is BreakpointsViewModel viewModel &&
            viewModel.RemoveBreakpointCommand.CanExecute(null)) {
            viewModel.RemoveBreakpointCommand.Execute(null);
        }
    }
}