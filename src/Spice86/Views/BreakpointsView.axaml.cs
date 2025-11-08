namespace Spice86.Views;
using Avalonia.Controls;
using Avalonia.Input;

using Spice86.ViewModels;

using System;

public partial class BreakpointsView : UserControl
{
    public BreakpointsView()
    {
        InitializeComponent();
        BreakpointsDataGrid.KeyUp += BreakpointsDataGrid_KeyUp;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) {
        if (DataContext is BreakpointsViewModel viewModel) {
            var controlViewModel = new MemoryBreakpointUserControlViewModel {
                ShowValueCondition = true,
                SelectedBreakpointType = viewModel.SelectedMemoryBreakpointType,
                BreakpointTypes = viewModel.MemoryBreakpointTypes,
                StartAddress = viewModel.MemoryBreakpointStartAddress,
                EndAddress = viewModel.MemoryBreakpointEndAddress,
                ValueCondition = viewModel.MemoryBreakpointValueCondition
            };
            
            // Set up two-way binding by updating parent when child changes
            controlViewModel.PropertyChanged += (s, args) => {
                if (args.PropertyName == nameof(MemoryBreakpointUserControlViewModel.SelectedBreakpointType)) {
                    viewModel.SelectedMemoryBreakpointType = controlViewModel.SelectedBreakpointType;
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
                if (args.PropertyName == nameof(BreakpointsViewModel.SelectedMemoryBreakpointType)) {
                    controlViewModel.SelectedBreakpointType = viewModel.SelectedMemoryBreakpointType;
                } else if (args.PropertyName == nameof(BreakpointsViewModel.MemoryBreakpointStartAddress)) {
                    controlViewModel.StartAddress = viewModel.MemoryBreakpointStartAddress;
                } else if (args.PropertyName == nameof(BreakpointsViewModel.MemoryBreakpointEndAddress)) {
                    controlViewModel.EndAddress = viewModel.MemoryBreakpointEndAddress;
                } else if (args.PropertyName == nameof(BreakpointsViewModel.MemoryBreakpointValueCondition)) {
                    controlViewModel.ValueCondition = viewModel.MemoryBreakpointValueCondition;
                }
            };
            
            MemoryBreakpointControl.DataContext = controlViewModel;
        }
    }

    private void BreakpointsDataGrid_KeyUp(object? sender, KeyEventArgs e) {
        if (e.Key == Key.Delete && DataContext is BreakpointsViewModel viewModel &&
            viewModel.RemoveBreakpointCommand.CanExecute(null)) {
            viewModel.RemoveBreakpointCommand.Execute(null);
        }
    }

    private void DataGrid_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if(DataContext is BreakpointsViewModel viewModel &&
            viewModel.EditSelectedBreakpointCommand.CanExecute(null)) {
            viewModel.EditSelectedBreakpointCommand.Execute(null);
        }
    }
}