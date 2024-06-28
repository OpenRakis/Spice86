using Avalonia.Controls;

namespace Spice86.Views;

public partial class StructureView : Window {
    public StructureView() {
        InitializeComponent();
    }

    private void AutoCompleteBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) {
        if (DataContext is not ViewModels.StructureViewModel structureViewModel || e.AddedItems.Count != 1) {
            return;
        }
        var selectedItem = e.AddedItems[0]?.ToString();
        structureViewModel.SelectStructure(selectedItem);
    }
}