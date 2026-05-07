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

    private void DataGrid_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if(DataContext is BreakpointsViewModel viewModel &&
            viewModel.EditSelectedBreakpointCommand.CanExecute(null)) {
            viewModel.EditSelectedBreakpointCommand.Execute(null);
        }
    }

    /// <summary>
    /// Workaround for a Dock.Avalonia DockControl crash where its PointerReleased
    /// handler calls e.GetPosition(...) on an AutoCompleteBox popup item that has
    /// already been detached from the visual tree when the popup closes. Marking the
    /// event as handled here prevents it from bubbling up to the outer DockControl.
    /// </summary>
    private void AutoCompleteBox_PointerReleased(object? sender, PointerReleasedEventArgs e) {
        e.Handled = true;
    }
}