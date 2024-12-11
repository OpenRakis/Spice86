namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;

public partial class BreakpointViewModel : ViewModelBase {
    private readonly Action _onReached;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private AddressBreakPoint? _breakPoint;

    public BreakpointViewModel(
        BreakpointsViewModel breakpointsViewModel,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
            uint address,
            BreakPointType type,
            bool isRemovedOnTrigger,
            Action onReached,
            string comment = "") {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        Address = address;
        Type = type;
        IsRemovedOnTrigger = isRemovedOnTrigger;
        if(IsRemovedOnTrigger) {
            _onReached = () => {
                breakpointsViewModel.RemoveBreakpointInternal(this);
                onReached();
            };
        } else {
            _onReached = onReached;
        }
        Comment = comment;
        Enable();
    }

    public BreakPointType Type { get; }

    //Can't get out of sync since GDB can't be used at the same time as the internal debugger
    private bool _isEnabled;

    public bool IsEnabled {
        get => _isEnabled;
        set {
            if (value) {
                Enable();
            } else {
                Disable();
            }
            SetProperty(ref _isEnabled, value);
        }
    }

    public bool IsRemovedOnTrigger { get; }

    public uint Address { get; }

    public void Toggle() {
        if (IsEnabled) {
            Disable();
        } else {
            Enable();
        }
    }

    [ObservableProperty]
    private string? _comment;

    private AddressBreakPoint GetOrCreateBreakpoint() {
        _breakPoint ??=
        new AddressBreakPoint(
            Type,
            Address,
            (_) => _onReached(),
            IsRemovedOnTrigger);
        return _breakPoint;
    }

    public void Enable() {
        if (IsEnabled) {
            return;
        }
        _emulatorBreakpointsManager.ToggleBreakPoint(GetOrCreateBreakpoint(), on: true);
        _isEnabled = true;
        OnPropertyChanged(nameof(IsEnabled));
    }

    public void Disable() {
        if (!IsEnabled) {
            return;
        }
        _emulatorBreakpointsManager.ToggleBreakPoint(GetOrCreateBreakpoint(), on: false);
        _isEnabled = false;
        OnPropertyChanged(nameof(IsEnabled));
    }

    internal bool IsFor(CpuInstructionInfo instructionInfo) {
        return Address == instructionInfo.Address;
    }
}