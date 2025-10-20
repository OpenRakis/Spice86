namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

public partial class BreakpointViewModel : ViewModelBase {
    private readonly Action _onReached;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private BreakPoint? _breakPoint;

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
        Enable();
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
        _breakPoint ??= new AddressBreakPoint(Type,
            Address, (_) => _onReached(),
            IsRemovedOnTrigger);
        return _breakPoint;
    }

    [RelayCommand]
    public void Enable() {
        if (IsEnabled) {
            return;
        }
        _emulatorBreakpointsManager.ToggleBreakPoint(GetOrCreateBreakpoint(),
            on: true);
        _isEnabled = true;
        OnPropertyChanged(nameof(IsEnabled));
    }

    [RelayCommand]
    public void Disable() {
        if (!IsEnabled) {
            return;
        }
        _emulatorBreakpointsManager.ToggleBreakPoint(GetOrCreateBreakpoint(),
            on: false);
        _isEnabled = false;
        OnPropertyChanged(nameof(IsEnabled));
    }
}
