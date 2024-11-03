namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;

using System;
using System.Collections.ObjectModel;

public partial class BreakpointsViewModel : ViewModelBase {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;
    private readonly IMemory _memory;

    public BreakpointsViewModel(IPauseHandler pauseHandler, IMemory memory, EmulatorBreakpointsManager emulatorBreakpointsManager) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _memory = memory;
        _pauseHandler = pauseHandler;
    }
    
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
        var breakpointViewModel = new BreakpointViewModel( _emulatorBreakpointsManager, addressBreakPoint);
        Breakpoints.Add(breakpointViewModel);
        SelectedBreakpoint = breakpointViewModel;
    }

    private bool RemoveBreakpointCanExecute() => SelectedBreakpoint is not null;


    [RelayCommand(CanExecute = nameof(RemoveBreakpointCanExecute))]
    private void RemoveBreakpoint() {
        if (SelectedBreakpoint is not null) {
            DeleteBreakpoint(SelectedBreakpoint);
        }
    }

    internal bool HasBreakpoint(CpuInstructionInfo instructionInfo) {
        return Breakpoints.Any(x => x.IsFor(instructionInfo));
    }

    internal void RemoveBreakpoint(CpuInstructionInfo selectedInstruction) {
        BreakpointViewModel? breakpoint = Breakpoints.FirstOrDefault(x => x.IsFor(selectedInstruction));
        if (breakpoint is not null) {
            DeleteBreakpoint(breakpoint);
        }
    }

    private void DeleteBreakpoint(BreakpointViewModel breakpoint) {
        breakpoint.Disable();
        Breakpoints.Remove(breakpoint);
    }
}