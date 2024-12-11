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

    public event Action<BreakpointViewModel>? BreakpointDeleted;
    public event Action<BreakpointViewModel>? BreakpointEnabled;
    public event Action<BreakpointViewModel>? BreakpointDisabled;
    
    [ObservableProperty]
    private ObservableCollection<BreakpointViewModel> _breakpoints = new();

    [RelayCommand(CanExecute = nameof(ToggleSelectedBreakpointCanExecute))]
    private void ToggleSelectedBreakpoint() {
        if (SelectedBreakpoint is not null) {
            SelectedBreakpoint.Toggle();
            if (SelectedBreakpoint.IsEnabled) {
                BreakpointEnabled?.Invoke(SelectedBreakpoint);
            } else {
                BreakpointDisabled?.Invoke(SelectedBreakpoint);
            }
        }
    }

    private bool ToggleSelectedBreakpointCanExecute() => SelectedBreakpoint is not null;
    
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveBreakpointCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleSelectedBreakpointCommand))]
    private BreakpointViewModel? _selectedBreakpoint;

    internal void AddUnconditionalBreakpoint(Action onReached, bool removedOnTrigger) {
        _emulatorBreakpointsManager.ToggleBreakPoint(
            new UnconditionalBreakPoint(
                BreakPointType.EXECUTION,
                (_) => onReached(),
                removedOnTrigger), on: true);
    }

    internal BreakpointViewModel AddAddressBreakpoint(
            uint address,
            BreakPointType type,
            bool isRemovedOnTrigger,
            Action onReached,
            string comment = "") {
        var breakpointViewModel = new BreakpointViewModel( 
            this,
            _emulatorBreakpointsManager,
            address, type, isRemovedOnTrigger, onReached, comment);
        Breakpoints.Add(breakpointViewModel);
        SelectedBreakpoint = breakpointViewModel;
        return breakpointViewModel;
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
        BreakpointDeleted?.Invoke(breakpoint);
    }
}