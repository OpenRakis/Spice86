namespace Spice86.ViewModels;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.VM.Breakpoint.Serializable;
using Spice86.Shared.Utils;
using Spice86.ViewModels.Messages;
using Spice86.ViewModels.Services;

using System.Collections.ObjectModel;

public partial class BreakpointsViewModel : ViewModelWithMemoryBreakpoints {
    private const string ExecutionBreakpoint = "Execution breakpoint";
    private const string MemoryRangeBreakpoint = "Memory range breakpoint";
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;
    private readonly IUIDispatcher _uiDispatcher;

    public BreakpointsViewModel(State state,
        IPauseHandler pauseHandler,
        IMessenger messenger,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IUIDispatcher uiDispatcher,
        IMemory memory) : base(state, memory) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _messenger = messenger;
        _uiDispatcher = uiDispatcher;
        SelectedBreakpointTypeTab = BreakpointTabs.FirstOrDefault();
        NotifySelectedBreakpointTypeChanged();
    }

    protected override void NotifyMemoryBreakpointCanExecuteChanged() {
        ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
    }

    private bool _mustRemoveSelectedBreakpoint;


    [RelayCommand(CanExecute = nameof(EditSelectedBreakpointCanExecute))]
    private void EditSelectedBreakpoint() {
        if (SelectedBreakpoint is not null) {
            _mustRemoveSelectedBreakpoint = true;
            switch (SelectedBreakpoint.Type) {
                case BreakPointType.CPU_EXECUTION_ADDRESS:
                    ExecutionAddressValue = ConvertUtils.ToHex32((uint)
                        SelectedBreakpoint.Address);
                    SelectedBreakpointTypeTab = BreakpointTabs.First(x => x.Header == "Execution");
                    break;
                case BreakPointType.CPU_CYCLES:
                    CyclesValue = SelectedBreakpoint.Address;
                    SelectedBreakpointTypeTab = BreakpointTabs.First(x => x.Header == "Cycles");
                    break;
                case BreakPointType.CPU_INTERRUPT:

                    InterruptNumber = ConvertUtils.ToHex32((uint)SelectedBreakpoint.Address);
                    SelectedBreakpointTypeTab = BreakpointTabs.First(x => x.Header == "Interrupt");
                    break;
                case BreakPointType.IO_ACCESS:
                    IoPortNumber = ConvertUtils.ToHex32((uint)SelectedBreakpoint.Address);
                    SelectedBreakpointTypeTab = BreakpointTabs.First(x => x.Header == "I/O Port");
                    break;
                case BreakPointType.MEMORY_ACCESS:
                case BreakPointType.MEMORY_READ:
                case BreakPointType.MEMORY_WRITE:
                    MemoryBreakpointStartAddress = ConvertUtils.ToHex32((uint)SelectedBreakpoint.Address);
                    MemoryBreakpointEndAddress = ConvertUtils.ToHex32((uint)SelectedBreakpoint.EndAddress);
                    SelectedMemoryBreakpointType = SelectedBreakpoint.Type;
                    // Note: Value condition cannot be recovered from the breakpoint function, so it's cleared when editing
                    MemoryBreakpointValueCondition = null;
                    SelectedBreakpointTypeTab = BreakpointTabs.First(x => x.Header == "Memory");
                    break;
            }
            CreatingBreakpoint = true;
        }
    }

    private bool EditSelectedBreakpointCanExecute() => SelectedBreakpoint is not null;

    public AvaloniaList<BreakpointTypeTabItemViewModel> BreakpointTabs { get; } = new AvaloniaList<BreakpointTypeTabItemViewModel>
    {
        new BreakpointTypeTabItemViewModel { Header = "Cycles", IsSelected = false },
        new BreakpointTypeTabItemViewModel { Header = "Memory", IsSelected = false },
        new BreakpointTypeTabItemViewModel { Header = "Execution", IsSelected = false },
        new BreakpointTypeTabItemViewModel { Header = "Interrupt", IsSelected = false },
        new BreakpointTypeTabItemViewModel { Header = "I/O Port", IsSelected = false }
    };

    private BreakpointTypeTabItemViewModel? _selectedBreakpointTypeTab;

    public BreakpointTypeTabItemViewModel? SelectedBreakpointTypeTab {
        get => _selectedBreakpointTypeTab;
        set {
            if (value is not null) {
                foreach (BreakpointTypeTabItemViewModel tab in BreakpointTabs) {
                    tab.IsSelected = tab == value;
                }
                SetProperty(ref _selectedBreakpointTypeTab, value);
                NotifySelectedBreakpointTypeChanged();
            }
        }
    }

    private void NotifySelectedBreakpointTypeChanged() {
        OnPropertyChanged(nameof(IsExecutionBreakpointSelected));
        OnPropertyChanged(nameof(IsMemoryBreakpointSelected));
        OnPropertyChanged(nameof(IsCyclesBreakpointSelected));
        OnPropertyChanged(nameof(IsInterruptBreakpointSelected));
        OnPropertyChanged(nameof(IsIoPortBreakpointSelected));
    }

    public State State => _state;

    public bool IsExecutionBreakpointSelected => BreakpointTabs.Any(x => x.Header == "Execution" && x.IsSelected);

    public bool IsMemoryBreakpointSelected => BreakpointTabs.Any(x => x.Header == "Memory" && x.IsSelected);

    public bool IsCyclesBreakpointSelected => BreakpointTabs.Any(x => x.Header == "Cycles" && x.IsSelected);

    public bool IsInterruptBreakpointSelected => BreakpointTabs.Any(x => x.Header == "Interrupt" && x.IsSelected);

    public bool IsIoPortBreakpointSelected => BreakpointTabs.Any(x => x.Header == "I/O Port" && x.IsSelected);

    [ObservableProperty]
    private bool _creatingBreakpoint;

    [RelayCommand]
    private void BeginCreateBreakpoint() {
        CreatingBreakpoint = true;
        CyclesValue = _state.Cycles;
        ExecutionAddressValue = State.IpSegmentedAddress.ToString();
        MemoryBreakpointStartAddress = State.IpSegmentedAddress.ToString();
        MemoryBreakpointEndAddress = State.IpSegmentedAddress.ToString();
    }

    private long? _cyclesValue;

    public long? CyclesValue {
        get => _cyclesValue;
        set {
            ValidateRequiredPropertyIsNotNull(value);
            SetProperty(ref _cyclesValue, value);
            ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _executionAddressValue;

    public string? ExecutionAddressValue {
        get => _executionAddressValue;
        set {
            ValidateAddressProperty(value, _state);
            ValidateMemoryAddressIsWithinLimit(_state, value);
            SetProperty(ref _executionAddressValue, value);
            ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _ioPortNumber = "0x0";

    public string? IoPortNumber {
        get => _ioPortNumber;
        set {
            ValidateAddressProperty(value, _state);
            SetProperty(ref _ioPortNumber, value);
            ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _interruptNumber = "0x0";

    public string? InterruptNumber {
        get => _interruptNumber;
        set {
            ValidateAddressProperty(value, _state);
            SetProperty(ref _interruptNumber, value);
            ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(ConfirmBreakpointCreationCanExecute))]
    private void ConfirmBreakpointCreation() {
        if (IsExecutionBreakpointSelected) {
            if (!AddressAndValueParser.TryParseAddressString(ExecutionAddressValue, _state, out uint? executionAddress)) {
                return;
            }
            BreakpointViewModel executionVm = AddAddressBreakpoint(
                executionAddress.Value,
                BreakPointType.CPU_EXECUTION_ADDRESS,
                false,
                () => {
                    PauseAndReportAddress(
                    ExecutionAddressValue);
                }, null, ExecutionBreakpoint);
            BreakpointCreated?.Invoke(executionVm);
        } else if (IsMemoryBreakpointSelected) {
            TryCreateMemoryBreakpointFromForm(CreateMemoryBreakpointAtAddress);
        } else if (IsCyclesBreakpointSelected) {
            if (CyclesValue is null) {
                return;
            }
            long cyclesValue = CyclesValue.Value;
            BreakpointViewModel cyclesVm = AddAddressBreakpoint(
                cyclesValue,
                BreakPointType.CPU_CYCLES,
                false,
                () => {
                    PauseAndReportCycles(CyclesValue.Value);
                }, null, "Cycles breakpoint");
            BreakpointCreated?.Invoke(cyclesVm);
        } else if (IsInterruptBreakpointSelected) {
            if (!AddressAndValueParser.TryParseAddressString(InterruptNumber, _state, out uint? interruptNumber)) {
                return;
            }
            BreakpointViewModel interruptVm = AddAddressBreakpoint(
                interruptNumber.Value,
                BreakPointType.CPU_INTERRUPT,
                false,
                () => {
                    PauseAndReportInterrupt(interruptNumber.Value);
                }, null, "Interrupt breakpoint");
            BreakpointCreated?.Invoke(interruptVm);
        } else if (IsIoPortBreakpointSelected) {
            if (!AddressAndValueParser.TryParseAddressString(IoPortNumber, _state, out uint? ioPortNumber)) {
                return;
            }
            BreakpointViewModel ioPortVm = AddAddressBreakpoint(
                ioPortNumber.Value,
                BreakPointType.IO_ACCESS,
                false,
                () => {
                    PauseAndReportIoPort((ushort)ioPortNumber.Value);
                }, null, "I/O Port breakpoint");
            BreakpointCreated?.Invoke(ioPortVm);
        }
        CreatingBreakpoint = false;
    }

    internal void CreateMemoryBreakpointAtAddress(uint startAddress, uint endAddress, BreakPointType type, Func<long, bool>? additionalTriggerCondition) {
        BreakpointViewModel breakpointVm = AddAddressRangeBreakpoint(
            startAddress,
            endAddress,
            type,
            false,
            () => {
                PauseAndReportAddressRange(MemoryBreakpointStartAddress, MemoryBreakpointEndAddress);
            }, additionalTriggerCondition, MemoryRangeBreakpoint);
        BreakpointCreated?.Invoke(breakpointVm);
    }

    private bool ConfirmBreakpointCreationCanExecute() {
        if (IsInterruptBreakpointSelected) {
            return !ScanForValidationErrors(nameof(InterruptNumber));
        } else if (IsIoPortBreakpointSelected) {
            return !ScanForValidationErrors(nameof(IoPortNumber));
        } else if (IsCyclesBreakpointSelected) {
            return !ScanForValidationErrors(nameof(CyclesValue));
        } else if (IsMemoryBreakpointSelected) {
            return HasNoMemoryBreakpointValidationErrors();
        } else if (IsExecutionBreakpointSelected) {
            return !ScanForValidationErrors(nameof(ExecutionAddressValue));
        }
        return false;
    }

    private void PauseAndReportAddressRange(string? startAddress, string? endAddress) {
        string message = $"Execution breakpoint was reached at address range {startAddress} - {endAddress}.";
        Pause(message);
    }

    private void PauseAndReportAddress(string? address) {
        string message = $"Execution breakpoint was reached at address {address}.";
        Pause(message);
    }

    private void PauseAndReportCycles(long cycles) {
        string message = $"Cycles breakpoint was reached at {cycles} cycles.";
        Pause(message);
    }

    private void PauseAndReportInterrupt(uint interruptNumber) {
        string message = $"Interrupt breakpoint was reached at interrupt 0x{interruptNumber:X2}.";
        Pause(message);
    }

    private void PauseAndReportIoPort(ushort ioPortAddress) {
        string message = $"I/O Port breakpoint was reached at 0x{ioPortAddress:X2}.";
        Pause(message);
    }

    private void Pause(string message) {
        _pauseHandler.RequestPause(message);
        _uiDispatcher.Post(() => {
            _messenger.Send(new StatusMessage(DateTime.Now, this, message));
        });
        // Wait here to block the emulation thread until user resumes
        // This ensures State.IP remains at the breakpoint location (e.g., INT instruction)
        _pauseHandler.WaitIfPaused();
    }

    [RelayCommand]
    private void CancelBreakpointCreation() {
        CreatingBreakpoint = false;
    }

    public event Action<BreakpointViewModel>? BreakpointDeleted;
    public event Action<BreakpointViewModel>? BreakpointEnabled;
    public event Action<BreakpointViewModel>? BreakpointCreated;
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
    [NotifyCanExecuteChangedFor(nameof(EditSelectedBreakpointCommand))]
    private BreakpointViewModel? _selectedBreakpoint;

    public void AddUnconditionalBreakpoint(Action onReached, bool removedOnTrigger) {
        _emulatorBreakpointsManager.ToggleBreakPoint(
            new UnconditionalBreakPoint(
                BreakPointType.CPU_EXECUTION_ADDRESS,
                (_) => onReached(),
                removedOnTrigger) { IsUserBreakpoint = true },
            on: true);
    }

    private void AddBreakpointInternal<T>(T breakpointViewModel) where T : BreakpointViewModel {
        Breakpoints.Add(breakpointViewModel);
        SelectedBreakpoint = breakpointViewModel;
        breakpointViewModel.Enable();
        BreakpointCreated?.Invoke(breakpointViewModel);
    }

    public BreakpointViewModel AddAddressBreakpoint(
            long trigger,
            BreakPointType type,
            bool isRemovedOnTrigger,
            Action onReached,
            Func<long, bool>? additionalTriggerCondition = null,
            string comment = "",
            string? conditionExpression = null) {
        return AddAddressRangeBreakpoint(trigger, trigger, type, isRemovedOnTrigger, onReached,
            additionalTriggerCondition, comment, conditionExpression);
    }

    public BreakpointViewModel AddAddressRangeBreakpoint(
            long trigger,
            long endTrigger,
            BreakPointType type,
            bool isRemovedOnTrigger,
            Action onReached,
            Func<long, bool>? additionalTriggerCondition,
            string comment = "",
            string? conditionExpression = null) {
        RemoveFirstIfEdited();
        var breakpointViewModel = new BreakpointViewModel(
                    this,
                    _emulatorBreakpointsManager,
                    trigger, endTrigger, type, isRemovedOnTrigger, onReached, additionalTriggerCondition, comment, conditionExpression);
        AddBreakpointInternal(breakpointViewModel);
        return breakpointViewModel;
    }

    private void RemoveFirstIfEdited() {
        if (_mustRemoveSelectedBreakpoint) {
            // Remove the existing breakpoint
            DeleteBreakpoint(SelectedBreakpoint);
            _mustRemoveSelectedBreakpoint = false;
        }
    }

    private bool RemoveBreakpointCanExecute() => SelectedBreakpoint is not null;

    [RelayCommand(CanExecute = nameof(RemoveBreakpointCanExecute))]
    private void RemoveBreakpoint() {
        DeleteBreakpoint(SelectedBreakpoint);
    }

    private bool AllBreakpointsCommandCanExecute() => Breakpoints.Any();

    [RelayCommand(CanExecute = nameof(AllBreakpointsCommandCanExecute))]
    private void RemoveAllBreakpoints() {
        while (Breakpoints.Count > 0) {
            DeleteBreakpoint(Breakpoints[0]);
        }
    }

    [RelayCommand(CanExecute = nameof(AllBreakpointsCommandCanExecute))]
    private void DisableAllBreakpoints() {
        foreach (BreakpointViewModel breakpoint in Breakpoints) {
            breakpoint.Disable();
        }
    }

    [RelayCommand(CanExecute = nameof(AllBreakpointsCommandCanExecute))]
    private void EnableAllBreakpoints() {
        foreach (BreakpointViewModel breakpoint in Breakpoints) {
            breakpoint.Enable();
        }
    }

    public void RemoveBreakpointInternal(BreakpointViewModel vm) {
        DeleteBreakpoint(vm);
    }

    private void DeleteBreakpoint(BreakpointViewModel? breakpoint) {
        if (breakpoint is null) {
            return;
        }
        breakpoint.Delete();
        Breakpoints.Remove(breakpoint);
        BreakpointDeleted?.Invoke(breakpoint);
    }

    /// Retrieves all execution breakpoints at the specified linear address.
    /// <param name="addressLinear">The linear address where execution breakpoints are to be retrieved.</param>
    /// <returns>An enumerable collection of breakpoints that are of type CPU_EXECUTION_ADDRESS and match the specified address.</returns>
    public IEnumerable<BreakpointViewModel> GetExecutionBreakPointsAtAddress(uint addressLinear) {
        return Breakpoints.Where(bp => bp.Address == addressLinear && bp.Type == BreakPointType.CPU_EXECUTION_ADDRESS);
    }

    public void RestoreBreakpoints(SerializableUserBreakpointCollection breakpointsData) {
        if (breakpointsData?.Breakpoints == null || breakpointsData.Breakpoints.Count == 0) {
            return;
        }

        foreach (SerializableUserBreakpoint breakpointData in breakpointsData.Breakpoints) {
            RestoreBreakpoint(breakpointData);
        }
    }

    private void RestoreBreakpoint(SerializableUserBreakpoint breakpointData) {
        Action onReached = () => { };

        switch (breakpointData.Type) {
            case BreakPointType.CPU_EXECUTION_ADDRESS:
                onReached = () => PauseAndReportAddress($"0x{breakpointData.Trigger:X}");
                break;
            case BreakPointType.CPU_CYCLES:
                onReached = () => PauseAndReportCycles(breakpointData.Trigger);
                break;
            case BreakPointType.CPU_INTERRUPT:
                onReached = () => PauseAndReportInterrupt((uint)breakpointData.Trigger);
                break;
            case BreakPointType.IO_ACCESS:
            case BreakPointType.IO_READ:
            case BreakPointType.IO_WRITE:
                onReached = () => PauseAndReportIoPort((ushort)breakpointData.Trigger);
                break;
            case BreakPointType.MEMORY_ACCESS:
            case BreakPointType.MEMORY_READ:
            case BreakPointType.MEMORY_WRITE:
                onReached = () => PauseAndReportAddress($"0x{breakpointData.Trigger:X}");
                break;
        }

        // Compile condition expression if present
        Func<long, bool>? condition = null;
        string? conditionExpression = breakpointData.ConditionExpression;
        if (!string.IsNullOrWhiteSpace(conditionExpression)) {
            try {
                Shared.Emulator.VM.Breakpoint.Expression.ExpressionParser parser = new();
                Shared.Emulator.VM.Breakpoint.Expression.IExpressionNode ast = parser.Parse(conditionExpression);
                condition = (address) => {
                    Core.Emulator.VM.Breakpoint.BreakpointExpressionContext context = new(_state, _memory, address);
                    return ast.Evaluate(context) != 0;
                };
            } catch (ArgumentException) {
                // If parsing fails, treat as unconditional and clear the expression
                conditionExpression = null;
            }
        }

        BreakpointViewModel breakpointVm = AddAddressBreakpoint(
            breakpointData.Trigger,
            breakpointData.Type,
            false,
            onReached,
            condition,
            ExecutionBreakpoint,
            conditionExpression);

        if (!breakpointData.IsEnabled) {
            breakpointVm.Disable();
        }
    }
}
