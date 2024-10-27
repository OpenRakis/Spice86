namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;

using System;
using System.Collections.ObjectModel;

public partial class BreakpointsViewModel : ViewModelBase {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;

    public BreakpointsViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
    }

    [ObservableProperty]
    private bool _showBreakpointCreationDialog = false;
    
    [ObservableProperty]
    private ObservableCollection<BreakpointViewModel> _breakpoints = new();

    [RelayCommand(CanExecute = nameof(ToggleSelectedBreakpointCanExecute))]
    private void ToggleSelectedBreakpoint() {
        if (SelectedBreakpoint is not null) {
            SelectedBreakpoint.Toggle();
        }
    }

    private bool ToggleSelectedBreakpointCanExecute() => SelectedBreakpoint is not null;
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveBreakpointCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleSelectedBreakpointCommand))]
    private BreakpointViewModel? _selectedBreakpoint;

    internal void AddAddressBreakpoint(AddressBreakPoint addressBreakPoint) {
        var breakpointViewModel = new BreakpointViewModel(_emulatorBreakpointsManager, addressBreakPoint);
        Breakpoints.Add(breakpointViewModel);
        SelectedBreakpoint = breakpointViewModel;
    }

    [RelayCommand]
    private void Create() {
        ShowBreakpointCreationDialog = true;
    }

    private bool RemoveBreakpointCanExecute() => SelectedBreakpoint is not null;


    [RelayCommand(CanExecute = nameof(RemoveBreakpointCanExecute))]
    private void RemoveBreakpoint() {
        if (SelectedBreakpoint is not null) {
            Breakpoints.Remove(SelectedBreakpoint);
        }
    }

    internal bool HasBreakpoint(CpuInstructionInfo instructionInfo) {
        return Breakpoints.Any(x => x.IsFor(instructionInfo));
    }

    internal void RemoveBreakpoint(CpuInstructionInfo selectedInstruction) {
        BreakpointViewModel? breakpoint = Breakpoints.FirstOrDefault(x => x.IsFor(selectedInstruction));
        if (breakpoint is not null) {
            breakpoint.Disable();
            Breakpoints.Remove(breakpoint);
        }
    }
}