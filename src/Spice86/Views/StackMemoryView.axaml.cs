namespace Spice86.Views;

using Avalonia.Controls;

using AvaloniaHex;

using Spice86.ViewModels;

public partial class StackMemoryView : UserControl {
    public StackMemoryView() {
        InitializeComponent();
        // Subscribe to the DataContextChanged event to ensure that the ViewModel's event handler
        // is connected to the HexEditor's Selection.RangeChanged event after the DataContext is set.
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Handles the DataContextChanged event for the MemoryView.
    /// This method ensures that the MemoryViewModel's OnSelectionRangeChanged method
    /// is subscribed to the HexEditor's Selection.RangeChanged event whenever the DataContext changes.
    /// This setup allows the MemoryViewModel to react to selection range changes in the HexEditor.
    /// </summary>
    /// <param name="sender">The source of the event, typically the MemoryView itself.</param>
    /// <param name="e">Event arguments, not used in this method.</param>
    private void OnDataContextChanged(object? sender, EventArgs e) {
        // Attempt to find the HexEditor control within the view.
        HexEditor? hexEditor = this.FindControl<HexEditor>("HexViewer");
        // If the HexEditor is found and the DataContext is of type MemoryViewModel,
        // unsubscribe to the Selection.RangeChanged event to avoid multiple subscriptions and subscribe.
        if (hexEditor != null && DataContext is MemoryViewModel viewModel) {
            hexEditor.Selection.RangeChanged -= viewModel.OnSelectionRangeChanged;
            hexEditor.Selection.RangeChanged += viewModel.OnSelectionRangeChanged;
        }
    }
}