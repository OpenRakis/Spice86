namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using AvaloniaHex;
using AvaloniaHex.Document;

using Spice86.Shared.Utils;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;

using System;
using System.ComponentModel;

public partial class MemoryView : UserControl {
    private MemoryViewModel? _currentViewModel;

    public MemoryView() {
        InitializeComponent();
        // Subscribe to the DataContextChanged event to ensure that the ViewModel's event handler
        // is connected to the HexEditor's Selection.RangeChanged event after the DataContext is set.
        DataContextChanged += OnDataContextChanged;

        this.HexViewer.DoubleTapped += OnHexViewerDoubleTapped;
        HexViewer.HexView.BytesPerLine = 16;
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
        // Attempt to find the HexEditor control within the view.
        HexEditor? hexEditor = this.FindControl<HexEditor>("HexViewer");
        if (_currentViewModel is not null) {
            if (hexEditor is not null) {
                hexEditor.Selection.RangeChanged -= _currentViewModel.OnSelectionRangeChanged;
            }
            _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _currentViewModel = null;
        }

        // If the HexEditor is found and the DataContext is of type MemoryViewModel,
        // subscribe once to selection and search result updates.
        if (hexEditor is not null && DataContext is MemoryViewModel viewModel) {
            hexEditor.Selection.RangeChanged += viewModel.OnSelectionRangeChanged;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _currentViewModel = viewModel;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (_currentViewModel is null || e.PropertyName != nameof(MemoryViewModel.AddressOFoundOccurence)) {
            return;
        }

        if (_currentViewModel.AddressOFoundOccurence is null) {
            return;
        }

        NavigateToFoundOccurrence(_currentViewModel, _currentViewModel.AddressOFoundOccurence.Value);
    }

    private void NavigateToFoundOccurrence(MemoryViewModel viewModel, uint foundAddress) {
        if (!AddressAndValueParser.TryParseAddressString(viewModel.StartAddress, viewModel.State, out uint? startAddress)) {
            return;
        }

        if (!AddressAndValueParser.TryParseAddressString(viewModel.EndAddress, viewModel.State, out uint? endAddress)) {
            return;
        }

        if (startAddress is not uint startAddressValue) {
            return;
        }

        if (endAddress is not uint endAddressValue) {
            return;
        }

        if (foundAddress < startAddressValue || foundAddress > endAddressValue) {
            uint visibleWindowLength = endAddressValue >= startAddressValue ? endAddressValue - startAddressValue : 0;
            ulong reframedEndAddress = (ulong)foundAddress + visibleWindowLength;
            if (reframedEndAddress > uint.MaxValue) {
                reframedEndAddress = uint.MaxValue;
            }

            viewModel.StartAddress = ConvertUtils.ToHex32(foundAddress);
            viewModel.EndAddress = ConvertUtils.ToHex32((uint)reframedEndAddress);

            if (!AddressAndValueParser.TryParseAddressString(viewModel.StartAddress, viewModel.State,
                    out uint? reframedStartAddress) || reframedStartAddress is not uint reframedStartAddressValue) {
                return;
            }

            startAddressValue = reframedStartAddressValue;

            if (foundAddress < startAddressValue) {
                return;
            }
        }

        ulong relativeByteIndex = foundAddress - startAddressValue;

        HexEditor? hexEditor = this.FindControl<HexEditor>("HexViewer");
        if (hexEditor is null) {
            return;
        }

        Dispatcher.UIThread.Post(() => {
            BitLocation start = new(relativeByteIndex);
            BitLocation end = new(relativeByteIndex + 1);
            hexEditor.Selection.Range = new BitRange(start, end);
            hexEditor.Caret.Location = start;

            MoveViewportToFoundByte(hexEditor, start, relativeByteIndex);

            Dispatcher.UIThread.Post(() => {
                MoveViewportToFoundByte(hexEditor, start, relativeByteIndex);
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Background);
    }

    private static void MoveViewportToFoundByte(HexEditor hexEditor, BitLocation start, ulong relativeByteIndex) {
        double bytesPerLine = hexEditor.HexView.ActualBytesPerLine;
        if (bytesPerLine > 0) {
            Vector scrollOffset = new(0.0, relativeByteIndex / bytesPerLine);
            hexEditor.Caret.HexView.ScrollOffset = scrollOffset;
        }

        hexEditor.HexView.BringIntoView(start);
    }
}