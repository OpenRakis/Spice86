namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;

using System;

public partial class BreakpointViewModel : ViewModelBase {
    private readonly BreakPoint _breakPoint;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;

    public BreakpointViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager, AddressBreakPoint breakPoint) {
        _breakPoint = breakPoint;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        IsEnabled = true;
        Address = breakPoint.Address;
    }

    public string Type => _breakPoint.BreakPointType.ToString();

    //Can't get out of sync since GDB can't be used at the same tiem as the internal debugger
    [ObservableProperty]
    private bool _isEnabled;

    public bool IsRemovedOnTrigger => _breakPoint.IsRemovedOnTrigger;

    public long Address { get; }

    public void Toggle() {
        if (IsEnabled) {
            Disable();
        } else {
            Enable();
        }
    }

    [RelayCommand]
    public void Enable() {
        _emulatorBreakpointsManager.ToggleBreakPoint(_breakPoint, on: true);
        IsEnabled = true;
    }

    [RelayCommand]
    public void Disable() {
        _emulatorBreakpointsManager.ToggleBreakPoint(_breakPoint, on: false);
        IsEnabled = false;
    }

    internal bool IsFor(CpuInstructionInfo instructionInfo) {
        return _breakPoint is AddressBreakPoint addressBreakPoint && addressBreakPoint.Address == instructionInfo.Address;
    }
}