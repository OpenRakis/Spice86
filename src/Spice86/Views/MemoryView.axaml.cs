namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;

using AvaloniaHex;

using Spice86.ViewModels;

using System;

public partial class MemoryView : UserControl {
    public MemoryView() {
        InitializeComponent();
        // Subscribe to the DataContextChanged event to ensure that the ViewModel's event handler
        // is connected to the HexEditor's Selection.RangeChanged event after the DataContext is set.
        DataContextChanged += OnDataContextChanged;

        this.HexViewer.DoubleTapped += OnHexViewerDoubleTapped;
    }

    private void OnHexViewerDoubleTapped(object? sender, TappedEventArgs e) {
        if(DataContext is MemoryViewModel viewModel &&
            viewModel.EditMemoryCommand.CanExecute(null)) {
            viewModel.EditMemoryCommand.Execute(null);
        }
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
            
            // Initialize the MemoryBreakpointUserControl ViewModel
            var controlViewModel = new MemoryBreakpointUserControlViewModel {
                ShowValueCondition = true,
                SelectedBreakpointType = viewModel.SelectedBreakpointType,
                BreakpointTypes = viewModel.BreakpointTypes,
                StartAddress = viewModel.MemoryBreakpointStartAddress,
                EndAddress = viewModel.MemoryBreakpointEndAddress,
                ValueCondition = viewModel.MemoryBreakpointValueCondition
            };
            
            // Set up two-way binding by updating parent when child changes
            controlViewModel.PropertyChanged += (s, args) => {
                if (args.PropertyName == nameof(MemoryBreakpointUserControlViewModel.SelectedBreakpointType)) {
                    viewModel.SelectedBreakpointType = controlViewModel.SelectedBreakpointType;
                } else if (args.PropertyName == nameof(MemoryBreakpointUserControlViewModel.StartAddress)) {
                    viewModel.MemoryBreakpointStartAddress = controlViewModel.StartAddress;
                } else if (args.PropertyName == nameof(MemoryBreakpointUserControlViewModel.EndAddress)) {
                    viewModel.MemoryBreakpointEndAddress = controlViewModel.EndAddress;
                } else if (args.PropertyName == nameof(MemoryBreakpointUserControlViewModel.ValueCondition)) {
                    viewModel.MemoryBreakpointValueCondition = controlViewModel.ValueCondition;
                }
            };
            
            // Set up the other direction - update child when parent changes
            viewModel.PropertyChanged += (s, args) => {
                if (args.PropertyName == nameof(MemoryViewModel.SelectedBreakpointType)) {
                    controlViewModel.SelectedBreakpointType = viewModel.SelectedBreakpointType;
                } else if (args.PropertyName == nameof(MemoryViewModel.MemoryBreakpointStartAddress)) {
                    controlViewModel.StartAddress = viewModel.MemoryBreakpointStartAddress;
                } else if (args.PropertyName == nameof(MemoryViewModel.MemoryBreakpointEndAddress)) {
                    controlViewModel.EndAddress = viewModel.MemoryBreakpointEndAddress;
                } else if (args.PropertyName == nameof(MemoryViewModel.MemoryBreakpointValueCondition)) {
                    controlViewModel.ValueCondition = viewModel.MemoryBreakpointValueCondition;
                }
            };
            
            MemoryBreakpointControl.DataContext = controlViewModel;
        }
    }
}