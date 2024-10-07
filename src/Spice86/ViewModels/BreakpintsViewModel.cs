namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;

using System.Collections.ObjectModel;

public partial class BreakpintsViewModel : ViewModelBase {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;

    public BreakpintsViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
    }
    
    [ObservableProperty]
    private ObservableCollection<BreakpointViewModel> _breakpointsViewModels = new();
    
    [ObservableProperty]
    private BreakpointViewModel? _currentBreakpointViewModel;

    [RelayCommand]
    private void AddAddressBreakpoint(AddressBreakPoint addressBreakPoint) {
        var breakpointViewModel = new BreakpointViewModel(_emulatorBreakpointsManager, addressBreakPoint);
        BreakpointsViewModels.Add(breakpointViewModel);
        CurrentBreakpointViewModel = BreakpointsViewModels.Last();
    }
    
    [RelayCommand]
    private void RemoveBreakpoint() {
        if (CurrentBreakpointViewModel is not null) {
            BreakpointsViewModels.Remove(CurrentBreakpointViewModel);
        }
    }
}