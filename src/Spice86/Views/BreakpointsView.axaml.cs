namespace Spice86.Views;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Spice86.ViewModels;

public partial class BreakpointsView : UserControl
{
    public BreakpointsView()
    {
        InitializeComponent();
        BreakpointsDataGrid.KeyUp += BreakpointsDataGrid_KeyUp;
        // Swallow PointerReleased on the suggestion AutoCompleteBoxes so it doesn't bubble
        // up to Dock.Avalonia's DockControl.ReleasedHandler, which crashes when its event
        // source is a popup item already detached from the visual tree (the popup closes
        // synchronously on selection). See https://github.com/wieslawsoltes/Dock issues
        // around "Control does not belong to a visual tree".
        InterruptAutoCompleteBox.AddHandler(InputElement.PointerReleasedEvent,
            StopPointerReleasedBubbling, RoutingStrategies.Bubble, handledEventsToo: true);
        IoPortAutoCompleteBox.AddHandler(InputElement.PointerReleasedEvent,
            StopPointerReleasedBubbling, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void StopPointerReleasedBubbling(object? sender, PointerReleasedEventArgs e) {
        e.Handled = true;
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
}