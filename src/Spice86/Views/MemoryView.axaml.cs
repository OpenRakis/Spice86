namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

using AvaloniaHex;

using Spice86.ViewModels;

using System;
using System.ComponentModel;

public partial class MemoryView : UserControl {
    public static readonly StyledProperty<string?> SelectedAddressProperty =
        AvaloniaProperty.Register<MemoryView, string?>(
            nameof(SelectedAddress),
            defaultBindingMode: BindingMode.TwoWay);

    private bool _isUpdatingSelectedAddress;
    private MemoryViewModel? _trackedViewModel;

    public string? SelectedAddress {
        get => GetValue(SelectedAddressProperty);
        set => SetValue(SelectedAddressProperty, value);
    }

    public MemoryView() {
        InitializeComponent();
        // Subscribe to the DataContextChanged event to ensure that the ViewModel's event handler
        // is connected to the HexEditor's Selection.RangeChanged event after the DataContext is set.
        DataContextChanged += OnDataContextChanged;
        SelectedAddressProperty.Changed.AddClassHandler<MemoryView>(OnSelectedAddressPropertyChanged);

        this.HexViewer.DoubleTapped += OnHexViewerDoubleTapped;
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
        if (_trackedViewModel is not null) {
            _trackedViewModel.PropertyChanged -= OnTrackedViewModelPropertyChanged;
        }

        // Attempt to find the HexEditor control within the view.
        HexEditor? hexEditor = this.FindControl<HexEditor>("HexViewer");
        // If the HexEditor is found and the DataContext is of type MemoryViewModel,
        // unsubscribe to the Selection.RangeChanged event to avoid multiple subscriptions and subscribe.
        if (hexEditor != null && DataContext is MemoryViewModel viewModel) {
            hexEditor.Selection.RangeChanged -= viewModel.OnSelectionRangeChanged;
            hexEditor.Selection.RangeChanged += viewModel.OnSelectionRangeChanged;
            _trackedViewModel = viewModel;
            _trackedViewModel.PropertyChanged += OnTrackedViewModelPropertyChanged;
            UpdateSelectedAddressFromViewModel(viewModel.SelectionRangeStartAddress);
        } else {
            _trackedViewModel = null;
        }
    }

    private void OnTrackedViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is not MemoryViewModel viewModel) {
            return;
        }

        if (e.PropertyName == nameof(MemoryViewModel.SelectionRangeStartAddress)) {
            UpdateSelectedAddressFromViewModel(viewModel.SelectionRangeStartAddress);
        }
    }

    private void UpdateSelectedAddressFromViewModel(string? address) {
        _isUpdatingSelectedAddress = true;
        try {
            SelectedAddress = address;
        } finally {
            _isUpdatingSelectedAddress = false;
        }
    }

    private void OnSelectedAddressChanged(string? address) {
        if (_isUpdatingSelectedAddress) {
            return;
        }

        if (_trackedViewModel is null || string.IsNullOrWhiteSpace(address)) {
            return;
        }

        if (!string.Equals(_trackedViewModel.StartAddress, address, StringComparison.OrdinalIgnoreCase)) {
            _trackedViewModel.StartAddress = address;
        }
    }

    private void OnSelectedAddressPropertyChanged(MemoryView sender, AvaloniaPropertyChangedEventArgs e) {
        string? address = e.NewValue as string;
        sender.OnSelectedAddressChanged(address);
    }
}