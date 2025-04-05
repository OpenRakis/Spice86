namespace Spice86.ViewModels;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Infrastructure;
using Spice86.Messages;
using Spice86.Models.Debugging;
using Spice86.Shared.Utils;

using System.Collections.ObjectModel;

public partial class BreakpointsViewModel : ViewModelBase {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;
    private readonly IUIDispatcher _uiDispatcher;
    private readonly State _state;

    public BreakpointsViewModel(State state,
        IPauseHandler pauseHandler,
        IMessenger messenger,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IUIDispatcher uiDispatcher) {
        _state = state;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _messenger = messenger;
        _uiDispatcher = uiDispatcher;
        SelectedBreakpointTypeTab = BreakpointTabs.FirstOrDefault();
        NotifySelectedBreakpointTypeChanged();
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
                    InterruptNumber = (int)SelectedBreakpoint.Address;
                    SelectedBreakpointTypeTab = BreakpointTabs.First(x => x.Header == "Interrupt");
                    break;
                case BreakPointType.IO_ACCESS:
                    IoPortNumber = (ushort)SelectedBreakpoint.Address;
                    SelectedBreakpointTypeTab = BreakpointTabs.First(x => x.Header == "I/O Port");
                    break;
                case BreakPointType.MEMORY_ACCESS:
                case BreakPointType.MEMORY_READ:
                case BreakPointType.MEMORY_WRITE:
                    MemoryBreakpointEndAddress = MemoryBreakpointStartAddress = ConvertUtils.ToHex32((uint)
                        SelectedBreakpoint.Address);
                    if (SelectedBreakpoint is BreakpointRangeViewModel range) {
                        MemoryBreakpointEndAddress = ConvertUtils.ToHex32((uint)
                            range.EndTrigger);
                    }
                    SelectedMemoryBreakpointType = SelectedBreakpoint.Type;
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
        ExecutionAddressValue = MemoryBreakpointStartAddress = MemoryBreakpointEndAddress =
            State.IpSegmentedAddress.ToString();
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
            SetProperty(ref _executionAddressValue, value);
            ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _memoryBreakpointStartAddress;

    public string? MemoryBreakpointStartAddress {
        get => _memoryBreakpointStartAddress;
        set {
            ValidateAddressProperty(value, _state);
            SetProperty(ref _memoryBreakpointStartAddress, value);
            ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _memoryBreakpointEndAddress;

    public string? MemoryBreakpointEndAddress {
        get => _memoryBreakpointEndAddress;
        set {
            if (SetProperty(ref _memoryBreakpointEndAddress, value)) {
                ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private ushort? _ioPortNumber;

    public ushort? IoPortNumber {
        get => _ioPortNumber;
        set {
            ValidateRequiredPropertyIsNotNull(value);
            SetProperty(ref _ioPortNumber, value);
            ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
        }
    }

    private int? _interruptNumber;

    public int? InterruptNumber {
        get => _interruptNumber;
        set {
            ValidateRequiredPropertyIsNotNull(value);
            SetProperty(ref _interruptNumber, value);
            ConfirmBreakpointCreationCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    private BreakPointType _selectedMemoryBreakpointType = BreakPointType.MEMORY_ACCESS;

    public BreakPointType[] MemoryBreakpointTypes => new[] {
        BreakPointType.MEMORY_ACCESS, BreakPointType.MEMORY_WRITE, BreakPointType.MEMORY_READ
    };

    [RelayCommand(CanExecute = nameof(ConfirmBreakpointCreationCanExecute))]
    private void ConfirmBreakpointCreation() {
        if (IsExecutionBreakpointSelected) {
            if (!TryParseAddressString(ExecutionAddressValue, _state, out uint? executionAddress)) {
                return;
            }
            BreakpointViewModel executionVm = AddAddressBreakpoint(
                executionAddress.Value,
                BreakPointType.CPU_EXECUTION_ADDRESS,
                false,
                () => {
                    PauseAndReportAddress(
               ExecutionAddressValue);
                }, "Execution breakpoint");
            BreakpointCreated?.Invoke(executionVm);
        } else if (IsMemoryBreakpointSelected) {
            if (TryParseAddressString(MemoryBreakpointStartAddress, _state, out uint? memorystartAddress) &&
                TryParseAddressString(MemoryBreakpointEndAddress, _state, out uint? memoryEndAddress)) {
                CreateMemoryBreakpointRangeAtAddresses(memorystartAddress.Value,
                    memoryEndAddress.Value);
            } else if (TryParseAddressString(MemoryBreakpointStartAddress, _state,
                out uint? memoryStartAddressAlone)) {
                CreateMemoryBreakpointAtAddress(memoryStartAddressAlone.Value);
            }
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
                }, "Cycles breakpoint");
            BreakpointCreated?.Invoke(cyclesVm);
        } else if (IsInterruptBreakpointSelected) {
            if (InterruptNumber is null) {
                return;
            }
            BreakpointViewModel interruptVm = AddAddressBreakpoint(
                InterruptNumber.Value,
                BreakPointType.CPU_INTERRUPT,
                false,
                () => {
                    PauseAndReportInterrupt(InterruptNumber.Value);
                }, "Interrupt breakpoint");
            BreakpointCreated?.Invoke(interruptVm);
        } else if (IsIoPortBreakpointSelected) {
            if (IoPortNumber is null) {
                return;
            }
            BreakpointViewModel ioPortVm = AddAddressBreakpoint(
                IoPortNumber.Value,
                BreakPointType.IO_ACCESS,
                false,
                () => {
                    PauseAndReportIoPort(IoPortNumber.Value);
                }, "I/O Port breakpoint");
            BreakpointCreated?.Invoke(ioPortVm);
        }
        CreatingBreakpoint = false;
    }

    internal void CreateMemoryBreakpointAtAddress(uint memoryAddress) {
        BreakpointViewModel breakpointVm = AddAddressBreakpoint(
            memoryAddress,
            SelectedMemoryBreakpointType,
            false,
            () => {
                PauseAndReportAddress(MemoryBreakpointStartAddress);
            }, "Memory breakpoint");
        BreakpointCreated?.Invoke(breakpointVm);
    }

    internal void CreateMemoryBreakpointRangeAtAddresses(uint startAddress, uint endAddress) {
        BreakpointViewModel breakpointVm = AddAddressRangeBreakpoint(
            startAddress,
            endAddress,
            SelectedMemoryBreakpointType,
            false,
            () => {
                PauseAndReportAddressRange(MemoryBreakpointStartAddress, MemoryBreakpointEndAddress);
            }, "Memory breakpoint");
        BreakpointCreated?.Invoke(breakpointVm);
    }

    private bool ConfirmBreakpointCreationCanExecute() {
        if (IsInterruptBreakpointSelected) {
            return InterruptNumber is not null;
        } else if (IsIoPortBreakpointSelected) {
            return IoPortNumber is not null;
        } else if (IsCyclesBreakpointSelected) {
            return CyclesValue is not null;
        } else if (IsMemoryBreakpointSelected) {
            return
                TryParseAddressString(MemoryBreakpointStartAddress, _state, out uint? _) &&
                TryParseAddressString(MemoryBreakpointEndAddress, _state, out uint? _);
        } else if (IsExecutionBreakpointSelected) {
            return TryParseAddressString(ExecutionAddressValue, _state, out uint? _);
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

    private void PauseAndReportInterrupt(int interruptNumber) {
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
                removedOnTrigger), on: true);
    }

    private void AddBreakpointInternal<T>(T breakpointViewModel) where T : BreakpointViewModel {
        Breakpoints.Add(breakpointViewModel);
        SelectedBreakpoint = breakpointViewModel;
        BreakpointCreated?.Invoke(breakpointViewModel);
    }

    public BreakpointViewModel AddAddressBreakpoint(
            long trigger,
            BreakPointType type,
            bool isRemovedOnTrigger,
            Action onReached,
            string comment = "") {
        RemoveFirstIfEdited();
        var breakpointViewModel = new BreakpointViewModel(
                    this,
                    _emulatorBreakpointsManager,
                    trigger, type, isRemovedOnTrigger, onReached, comment);
        AddBreakpointInternal(breakpointViewModel);
        return breakpointViewModel;
    }

    public BreakpointRangeViewModel AddAddressRangeBreakpoint(
            long trigger,
            long endTrigger,
            BreakPointType type,
            bool isRemovedOnTrigger,
            Action onReached,
            string comment = "") {
        RemoveFirstIfEdited();
        var breakpointViewModel = new BreakpointRangeViewModel(
                    this,
                    _emulatorBreakpointsManager,
                    trigger, endTrigger, type, isRemovedOnTrigger, onReached, comment);
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

    public BreakpointViewModel? GetBreakpoint(CpuInstructionInfo instructionInfo) {
        return Breakpoints.FirstOrDefault(x => x.IsForCpuInstruction(instructionInfo));
    }

    private bool RemoveBreakpointCanExecute() => SelectedBreakpoint is not null;

    [RelayCommand(CanExecute = nameof(RemoveBreakpointCanExecute))]
    private void RemoveBreakpoint() {
        DeleteBreakpoint(SelectedBreakpoint);
    }

    public void RemoveBreakpointInternal(BreakpointViewModel vm) {
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
