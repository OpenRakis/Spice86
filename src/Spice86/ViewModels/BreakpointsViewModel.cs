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

    internal void AddAddressBreakpoint(
            long address,
            BreakPointType type,
            bool isRemovedOnTrigger,
            Action onReached) {
        var breakpointViewModel = new BreakpointViewModel( 
            this,
            _emulatorBreakpointsManager,
            address, type, isRemovedOnTrigger, onReached);
        Breakpoints.Add(breakpointViewModel);
        SelectedBreakpoint = breakpointViewModel;
    }

    internal BreakpointViewModel? GetBreakpoint(CpuInstructionInfo instructionInfo) {
        return Breakpoints.FirstOrDefault(x => x.IsFor(instructionInfo));
    }

    private bool RemoveBreakpointCanExecute() => SelectedBreakpoint is not null;


    [RelayCommand(CanExecute = nameof(RemoveBreakpointCanExecute))]
    private void RemoveBreakpoint() {
        DeleteBreakpoint(SelectedBreakpoint);
    }

    internal void RemoveBreakpointInternal(BreakpointViewModel vm) {
        DeleteBreakpoint(vm);
    }

    private void DeleteBreakpoint(BreakpointViewModel? breakpoint) {
        if (breakpoint is null) {
            return;
        }
        breakpoint.Disable();
        Breakpoints.Remove(breakpoint);
        BreakpointDeleted?.Invoke();
    }
}