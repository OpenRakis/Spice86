namespace Spice86.ViewModels;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;

public partial class BreakpointViewModel : ViewModelBase {
    private readonly BreakPoint _breakPoint;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;

    public BreakpointViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager, BreakPoint breakPoint) {
        _breakPoint = breakPoint;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
    }

    [RelayCommand]
    private void Enable() => _emulatorBreakpointsManager.ToggleBreakPoint(_breakPoint, on: true);
    
    [RelayCommand]
    private void Disable() => _emulatorBreakpointsManager.ToggleBreakPoint(_breakPoint, on: false);
}