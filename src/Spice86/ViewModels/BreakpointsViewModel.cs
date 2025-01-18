namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Infrastructure;
using Spice86.Messages;
using Spice86.Models.Debugging;

using System.Collections.ObjectModel;

public partial class BreakpointsViewModel : ViewModelWithErrorDialog {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;

    public BreakpointsViewModel(IPauseHandler pauseHandler,
        IMessenger messenger,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard) : base(uiDispatcher, textClipboard) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _messenger = messenger;
    }

    [ObservableProperty]
    private bool _isExecutionBreakpointSelected;

    [ObservableProperty]
    private bool _isMemoryBreakpointSelected;

    [ObservableProperty]
    private bool _isCyclesBreakpointSelected;

    [ObservableProperty]
    private bool _creatingBreakpoint;

    [RelayCommand]
    private void BeginCreateBreakpoint() {
        CreatingBreakpoint = true;
    }

    [ObservableProperty]
    private ulong? _cyclesValue;

    [ObservableProperty]
    private uint? _executionAddressValue;

    [ObservableProperty]
    private uint? _memoryAddressValue;

    [ObservableProperty]
    private BreakPointType _selectedMemoryBreakpointType = BreakPointType.MEMORY_ACCESS;

    public BreakPointType[] MemoryBreakpointTypes => [
        BreakPointType.MEMORY_ACCESS, BreakPointType.MEMORY_WRITE, BreakPointType.MEMORY_READ];

    [RelayCommand]
    private void ConfirmBreakpointCreation() {
        if (IsExecutionBreakpointSelected) {
            if (ExecutionAddressValue is null) {
                return;
            }
            uint executionValue = ExecutionAddressValue.Value;
            BreakpointViewModel executionVm = AddAddressBreakpoint(
                executionValue,
                BreakPointType.CPU_EXECUTION_ADDRESS,
                false,
                () => {
                    PauseAndReportAddress(executionValue);
                }, "Execution breakpoint");
            BreakpointCreated?.Invoke(executionVm);
        } else if (IsMemoryBreakpointSelected) {
            if (MemoryAddressValue is null) {
                return;
            }
            uint memValue = MemoryAddressValue.Value;
            BreakpointViewModel memoryVm = AddAddressBreakpoint(
                memValue,
                SelectedMemoryBreakpointType,
                false,
                () => {
                    PauseAndReportAddress(memValue);
                }, "Memory breakpoint");
            BreakpointCreated?.Invoke(memoryVm);
        } else if (IsCyclesBreakpointSelected) {
            if (CyclesValue is null) {
                return;
            }
        }
        CreatingBreakpoint = false;
    }

    private void PauseAndReportAddress(long address) {
        string message = $"Execution breakpoint was reached at address {address}.";
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
    private BreakpointViewModel? _selectedBreakpoint;

    public void AddUnconditionalBreakpoint(Action onReached, bool removedOnTrigger) {
        _emulatorBreakpointsManager.ToggleBreakPoint(
            new UnconditionalBreakPoint(
                BreakPointType.CPU_EXECUTION_ADDRESS,
                (_) => onReached(),
                removedOnTrigger), on: true);
    }

    public BreakpointViewModel AddAddressBreakpoint(
            uint address,
            BreakPointType type,
            bool isRemovedOnTrigger,
            Action onReached,
            string comment = "") {
        var breakpointViewModel = new BreakpointViewModel(
            this,
            _emulatorBreakpointsManager,
            address, type, isRemovedOnTrigger, onReached, comment);
        Breakpoints.Add(breakpointViewModel);
        SelectedBreakpoint = breakpointViewModel;
        return breakpointViewModel;
    }

    public BreakpointViewModel? GetBreakpoint(CpuInstructionInfo instructionInfo) {
        return Breakpoints.FirstOrDefault(x => x.IsFor(instructionInfo));
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