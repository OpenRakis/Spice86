using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using Spice86.ViewModels;

namespace Spice86.Views;

public partial class DisassemblyView : UserControl {
    public DisassemblyView() {
        InitializeComponent();
        FunctionComboBox.SelectionChanged += FunctionComboBox_SelectionChanged;
        DisassemblyDataGrid.KeyUp += DisassemblyDataGrid_KeyUp;
    }

    private void DisassemblyDataGrid_KeyUp(object? sender, KeyEventArgs e) {
        if (DataContext is DisassemblyViewModel viewModel) {
            if(e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control &&
                viewModel.CopyLineCommand.CanExecute(null)) {
                viewModel.CopyLineCommand.Execute(null);
            }
            if (e.Key == Key.F2 &&
                viewModel.CreateExecutionBreakpointHereCommand.CanExecute(null)) {
                viewModel.CreateExecutionBreakpointHereCommand.Execute(null);
            }
            if (e.Key == Key.Delete &&
                viewModel.RemoveExecutionBreakpointHereCommand.CanExecute(null)) {
                viewModel.RemoveExecutionBreakpointHereCommand.Execute(null);
            }
        }
    }

    private void FunctionComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
        if(this.DataContext is DisassemblyViewModel viewModel) {
            viewModel.GoToFunctionCommand.Execute(FunctionComboBox.SelectedItem);
        }
    }
}