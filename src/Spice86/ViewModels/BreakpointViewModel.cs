namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;

public partial class BreakpointViewModel : ViewModelBase {
    private readonly BreakPoint _breakPoint;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;

    public BreakpointViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager, AddressBreakPoint breakPoint) {
        _breakPoint = breakPoint;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        IsEnabled = true;
        Address = breakPoint.Address;
    }

    public BreakPointType Type => _breakPoint.BreakPointType;

    //Can't get out of sync since GDB can't be used at the same time as the internal debugger
    private bool _isEnabled;

    public bool IsEnabled {
        get => _isEnabled;
        set {
            if(SetProperty(ref _isEnabled, value)) {
                if (value) {
                    Enable();
                } else {
                    Disable();
                }
            }
        }
    }

    public bool IsRemovedOnTrigger => _breakPoint.IsRemovedOnTrigger;

    public long Address { get; }

    public void Toggle() {
        if (IsEnabled) {
            Disable();
        } else {
            Enable();
        }
    }

    public void Enable() {
        _emulatorBreakpointsManager.ToggleBreakPoint(_breakPoint, on: true);
        _isEnabled = true;
        OnPropertyChanged(nameof(IsEnabled));
    }

    public void Disable() {
        _emulatorBreakpointsManager.ToggleBreakPoint(_breakPoint, on: false);
        _isEnabled = false;
        OnPropertyChanged(nameof(IsEnabled));
    }

    internal bool IsFor(CpuInstructionInfo instructionInfo) {
        return _breakPoint is AddressBreakPoint addressBreakPoint && addressBreakPoint.Address == instructionInfo.Address;
    }
}