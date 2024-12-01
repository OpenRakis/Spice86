namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;

using System.Collections.ObjectModel;

public partial class BreakpointsViewModel : ViewModelBase {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;

    public BreakpointsViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
    }

    public event Action? BreakpointDeleted;
    public event Action? BreakpointCreated;
    public event Action? BreakpointEnabled;
    public event Action? BreakpointDisabled;
    
    [ObservableProperty]
    private ObservableCollection<BreakpointViewModel> _breakpoints = new();

    [RelayCommand(CanExecute = nameof(ToggleSelectedBreakpointCanExecute))]
    private void ToggleSelectedBreakpoint() {
        if (SelectedBreakpoint is not null) {
            SelectedBreakpoint.Toggle();
            if (SelectedBreakpoint.IsEnabled) {
                BreakpointEnabled?.Invoke();
            } else {
                BreakpointDisabled?.Invoke();
            }
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
        SelectedBreakpoint.Enable();
    }

    private bool RemoveBreakpointCanExecute() => SelectedBreakpoint is not null;


    [RelayCommand(CanExecute = nameof(RemoveBreakpointCanExecute))]
    private void RemoveBreakpoint() {
        if (SelectedBreakpoint is not null) {
            DeleteBreakpoint(SelectedBreakpoint);
            BreakpointDeleted?.Invoke();
        }
    }

    internal void RemoveUserExecutionBreakpoint(CpuInstructionInfo instructionInfo) {
        DeleteBreakpoint(Breakpoints.FirstOrDefault(x => x.IsFor(instructionInfo) && x is
            { IsRemovedOnTrigger: false, Type: BreakPointType.EXECUTION }));
    }

    internal bool HasUserExecutionBreakpoint(CpuInstructionInfo instructionInfo) {
        return Breakpoints.Any(x => x.IsFor(instructionInfo) && x is
            { IsRemovedOnTrigger: false, Type: BreakPointType.EXECUTION });
    }

    private void DeleteBreakpoint(BreakpointViewModel? breakpoint) {
        if (breakpoint is null) {
            return;
        }
        breakpoint.Disable();
        Breakpoints.Remove(breakpoint);
    }
}