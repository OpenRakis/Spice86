namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;

using AvaloniaHex;

using Spice86.ViewModels;

using System;
using System.ComponentModel;

public partial class MemoryView : UserControl {
    private MemoryViewModel? _trackedViewModel;

    public MemoryView() {
        InitializeComponent();
        // Subscribe to the DataContextChanged event to ensure that the ViewModel's event handler
        // is connected to the HexEditor's Selection.RangeChanged event after the DataContext is set.
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        this.HexViewer.DoubleTapped += OnHexViewerDoubleTapped;
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) {
        HexEditor? hexEditor = this.FindControl<HexEditor>("HexViewer");

        if (_trackedViewModel is not null) {
            _trackedViewModel.PropertyChanged -= OnTrackedViewModelPropertyChanged;
            if (hexEditor is not null) {
                hexEditor.Selection.RangeChanged -= _trackedViewModel.OnSelectionRangeChanged;
            }

            _trackedViewModel = null;
        }
    }

    private void OnHexViewerDoubleTapped(object? sender, TappedEventArgs e) {
        if (DataContext is MemoryViewModel viewModel &&
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
        HexEditor? hexEditor = this.FindControl<HexEditor>("HexViewer");

        if (_trackedViewModel is not null) {
            _trackedViewModel.PropertyChanged -= OnTrackedViewModelPropertyChanged;
            if (hexEditor is not null) {
                hexEditor.Selection.RangeChanged -= _trackedViewModel.OnSelectionRangeChanged;
            }
        }

        if (hexEditor != null && DataContext is MemoryViewModel viewModel) {
            hexEditor.Selection.RangeChanged += viewModel.OnSelectionRangeChanged;
            _trackedViewModel = viewModel;
            _trackedViewModel.PropertyChanged += OnTrackedViewModelPropertyChanged;
        } else {
            _trackedViewModel = null;
        }
    }

    private void OnTrackedViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is not MemoryViewModel viewModel) {
            return;
        }

        if (e.PropertyName == nameof(MemoryViewModel.SelectionRangeStartAddress) &&
            !string.IsNullOrWhiteSpace(viewModel.SelectionRangeStartAddress) &&
            !string.Equals(viewModel.StartAddress, viewModel.SelectionRangeStartAddress, StringComparison.OrdinalIgnoreCase)) {
            viewModel.StartAddress = viewModel.SelectionRangeStartAddress;
        }
    }
}