using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Spice86.ViewModels;

namespace Spice86.Views;

public partial class DisassemblyView : UserControl {
    public DisassemblyView() {
        InitializeComponent();
        FunctionComboBox.SelectionChanged += FunctionComboBox_SelectionChanged;
    }

    private void FunctionComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
        if(this.DataContext is DisassemblyViewModel viewModel) {
            viewModel.GoToFunctionCommand.Execute(FunctionComboBox.SelectedItem);
        }
    }
}