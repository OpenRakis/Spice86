namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

public partial class BreakpointViewModel : ViewModelBase {
    protected readonly Action _onReached;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    protected BreakPoint? _breakPoint;

    public BreakpointViewModel(
        BreakpointsViewModel breakpointsViewModel,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        long trigger,
        BreakPointType type,
        bool isRemovedOnTrigger,
        Action onReached,
        string comment = "") {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        Address = trigger;
        Type = type;
        IsRemovedOnTrigger = isRemovedOnTrigger;
        if (IsRemovedOnTrigger) {
            _onReached = () => {
                breakpointsViewModel.RemoveBreakpointInternal(this);
                onReached();
            };
        } else {
            _onReached = onReached;
        }
        Comment = comment;
        Parameter = $"0x{trigger:X2}";
    }

    [ObservableProperty]
    private string _parameter;

    public SegmentedAddress? SegmentedAddress { get; }

    public Action OnReached => _onReached;

    public BreakPointType Type { get; }

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

    public long Address { get; }

    public void Toggle() {
        if (IsEnabled) {
            Disable();
        } else {
            Enable();
        }
    }

    [ObservableProperty]
    private string? _comment;

    private BreakPoint GetOrCreateBreakpoint() {
        _breakPoint ??= CreateBreakpointWithAddress(Address);
        _breakPoint.IsUserBreakpoint = true;
        return _breakPoint;
    }

    protected AddressBreakPoint CreateBreakpointWithAddress(long address) {
        AddressBreakPoint bp = new AddressBreakPoint(Type, address, _ => _onReached(), IsRemovedOnTrigger);
        bp.IsUserBreakpoint = true;
        return bp;
    }

    [RelayCommand]
    public virtual void Enable() {
        if (IsEnabled) {
            return;
        }
        EnableInternal(GetOrCreateBreakpoint());
        OnPropertyChanged(nameof(IsEnabled));
    }

    protected void EnableInternal(BreakPoint breakpoint) {
        _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, on: true);
        _isEnabled = true;
    }

    protected void DisableInternal(BreakPoint breakpoint) {
        _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, on: false);
        _isEnabled = false;
    }

    [RelayCommand]
    public virtual void Disable() {
        if (!IsEnabled) {
            return;
        }
        DisableInternal(GetOrCreateBreakpoint());
        OnPropertyChanged(nameof(IsEnabled));
    }
}
