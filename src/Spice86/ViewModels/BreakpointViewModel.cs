namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

public partial class BreakpointViewModel : ViewModelBase {
    private readonly List<BreakPoint> _breakpoints = new List<BreakPoint>();
    private readonly Action _onReached;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly string? _conditionExpression;

    public BreakpointViewModel(
        BreakpointsViewModel breakpointsViewModel,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        long trigger,
        long endAddress,
        BreakPointType type,
        bool isRemovedOnTrigger,
        Action onReached,
        Func<long, bool>? additionalTriggerCondition,
        string comment,
        string? conditionExpression) {
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
        EndAddress = endAddress;
        _conditionExpression = conditionExpression;
        for (long i = Address; i <= EndAddress; i++) {
            AddressBreakPoint breakpoint = CreateBreakpointWithAddressAndCondition(i, additionalTriggerCondition);
            breakpoint.IsEnabled = true;
            _emulatorBreakpointsManager.ToggleBreakPoint(breakpoint, on: breakpoint.IsEnabled);
            _breakpoints.Add(breakpoint);
        }
        _isEnabled = true;
    }

    /// <summary>
    /// Creates a wildcard <see cref="BreakpointViewModel"/> backed by a single
    /// <see cref="UnconditionalBreakPoint"/> that fires on every event of the given
    /// <paramref name="type"/> (e.g. every interrupt or every I/O access).
    /// </summary>
    internal BreakpointViewModel(
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        BreakPointType type,
        Action onReached,
        string comment) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        Address = -1;
        EndAddress = -1;
        Type = type;
        IsRemovedOnTrigger = false;
        IsWildcard = true;
        _onReached = onReached;
        Comment = comment;
        Parameter = "*";
        _conditionExpression = null;
        UnconditionalBreakPoint bp = new(type, _ => onReached(), removeOnTrigger: false) { IsUserBreakpoint = true };
        bp.IsEnabled = true;
        _emulatorBreakpointsManager.ToggleBreakPoint(bp, on: true);
        _breakpoints.Add(bp);
        _isEnabled = true;
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

    /// <summary>
    /// True when this breakpoint was created as a wildcard ("*") and triggers on every
    /// event of its <see cref="Type"/> (e.g. every interrupt or every I/O port access).
    /// In that case, <see cref="Address"/> / <see cref="EndAddress"/> are not meaningful
    /// and <see cref="Parameter"/> is "*".
    /// </summary>
    public bool IsWildcard { get; }

    public long Address { get; }

    public long EndAddress { get; }

    public void Toggle() {
        if (IsEnabled) {
            Disable();
        } else {
            Enable();
        }
    }

    [ObservableProperty]
    private string? _comment;

    /// <summary>
    /// Gets the condition expression for this breakpoint, if any.
    /// </summary>
    public string? ConditionExpression => _conditionExpression;

    private BreakPoint GetOrCreateBreakpoint(Func<long, bool>? additionalTriggerCondition) {
        AddressBreakPoint breakPoint = CreateBreakpointWithAddressAndCondition(Address, additionalTriggerCondition);
        breakPoint.IsUserBreakpoint = true;
        return breakPoint;
    }

    protected AddressBreakPoint CreateBreakpointWithAddressAndCondition(long address, Func<long, bool>? additionalTriggerCondition) {
        AddressBreakPoint bp = new AddressBreakPoint(Type, address, _ => _onReached(), IsRemovedOnTrigger, additionalTriggerCondition, _conditionExpression);
        bp.IsUserBreakpoint = true;
        return bp;
    }

    [RelayCommand]
    public void Enable() {
        if (IsEnabled) {
            return;
        }
        foreach (BreakPoint breakpoint in _breakpoints) {
            breakpoint.IsEnabled = true;
        }
        _isEnabled = true;
        OnPropertyChanged(nameof(IsEnabled));
    }

    [RelayCommand]
    public void Disable() {
        if (!IsEnabled) {
            return;
        }
        foreach (BreakPoint breakpoint in _breakpoints) {
            breakpoint.IsEnabled = false;
        }
        _isEnabled = false;

        OnPropertyChanged(nameof(IsEnabled));
    }

    internal void Delete() {
        _emulatorBreakpointsManager.RemoveBreakpoints(_breakpoints);
    }
}
