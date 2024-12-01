using Avalonia.Controls;
using Avalonia.Input;

using Spice86.ViewModels;

namespace Spice86.Views;

public partial class DisassemblyView : UserControl {
    public DisassemblyView() {
        InitializeComponent();
        FunctionComboBox.SelectionChanged += FunctionComboBox_SelectionChanged;
        SegmentedStartAddressTextBox.KeyUp += SegmentedStartAddressTextBox_KeyUp;
        LinearStartAddressTextBox.KeyUp += LinearStartAddressTextBox_KeyUp;
        DisassemblyDataGrid.KeyUp += DisassemblyDataGrid_KeyUp;
        NumberOfInstructionsShownNumericUpDown.KeyUp += NumberOfInstructionsShownNumericUpDown_KeyUp;
    }

    private void NumberOfInstructionsShownNumericUpDown_KeyUp(object? sender, KeyEventArgs e) {
        if (DataContext is DisassemblyViewModel viewModel && e.Key == Key.Enter &&
            viewModel.UpdateDisassemblyCommand.CanExecute(null)) {
            viewModel.UpdateDisassemblyCommand.Execute(null);
        }
    }

    private void LinearStartAddressTextBox_KeyUp(object? sender, KeyEventArgs e) {
        ExecUpdateDisassemblyCommand(e, true);
    }

    private void ExecUpdateDisassemblyCommand(KeyEventArgs e, bool isUsingLinearAddressing) {
        if (DataContext is DisassemblyViewModel viewModel && e.Key == Key.Enter) {
            viewModel.IsUsingLinearAddressing = isUsingLinearAddressing;
            if (viewModel.UpdateDisassemblyCommand.CanExecute(null)) {
                viewModel.UpdateDisassemblyCommand.Execute(null);
            }
        }
    }

    private void SegmentedStartAddressTextBox_KeyUp(object? sender, KeyEventArgs e) {
        ExecUpdateDisassemblyCommand(e, false);

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