namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;

using Spice86.Messages;
using Spice86.Models;
using Spice86.ViewModels;

public partial class StructureView : Window {
    public StructureView() {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) {
        if (DataContext is StructureViewModel viewModel) {
            viewModel.RequestScrollToAddress += ViewModel_RequestScrollToAddress;
        }
    }

    private void ViewModel_RequestScrollToAddress(object? sender, AddressChangedMessage e) {
        var scrollOffset = new Vector(0.0, (double)e.Address / StructureHexEditor.HexView.ActualBytesPerLine);
        StructureHexEditor.Caret.HexView.ScrollOffset = scrollOffset;
    }
}