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

using System.Collections.ObjectModel;

public partial class TabItemViewModel : ViewModelBase {
    [ObservableProperty]
    private string? _header;
    
    [ObservableProperty]
    private bool _isSelected;
}

public partial class BreakpointsViewModel : ViewModelWithErrorDialog
{
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;
    private readonly State _state;

    public BreakpointsViewModel(State state,
        IPauseHandler pauseHandler,
        IMessenger messenger,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IUIDispatcher uiDispatcher,
        ITextClipboard textClipboard) : base(uiDispatcher, textClipboard)
    {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _messenger = messenger;
        _state = state;
        _selectedBreakpointTypeTab = BreakpointTabs[0];
    }

    public AvaloniaList<TabItemViewModel> BreakpointTabs { get; } = new AvaloniaList<TabItemViewModel>
    {
        new TabItemViewModel { Header = "Cycles", IsSelected = false },
        new TabItemViewModel { Header = "Memory", IsSelected = false },
        new TabItemViewModel { Header = "Execution", IsSelected = false },
        new TabItemViewModel { Header = "Interrupt", IsSelected = false },
        new TabItemViewModel { Header = "I/O Port", IsSelected = false }
    };

    private TabItemViewModel _selectedBreakpointTypeTab;

    public TabItemViewModel? SelectedBreakpointTypeTab {
        get => _selectedBreakpointTypeTab;
        set {
            if(value is not null) {
                foreach (TabItemViewModel tab in BreakpointTabs) {
                    tab.IsSelected = tab == value;
                }
                SetProperty(ref _selectedBreakpointTypeTab, value);
                OnPropertyChanged(nameof(IsExecutionBreakpointSelected));
                OnPropertyChanged(nameof(IsMemoryBreakpointSelected));
                OnPropertyChanged(nameof(IsCyclesBreakpointSelected));
                OnPropertyChanged(nameof(IsInterruptBreakpointSelected));
                OnPropertyChanged(nameof(IsIoPortBreakpointSelected));
            }
        }
    }

    public State State => _state;

    public bool IsExecutionBreakpointSelected => BreakpointTabs[2].IsSelected;

    public bool IsMemoryBreakpointSelected => BreakpointTabs[1].IsSelected;

    public bool IsCyclesBreakpointSelected => BreakpointTabs[0].IsSelected;

    public bool IsInterruptBreakpointSelected => BreakpointTabs[3].IsSelected;

    public bool IsIoPortBreakpointSelected => BreakpointTabs[4].IsSelected;

    [ObservableProperty]
    private bool _creatingBreakpoint;

    [RelayCommand]
    private void BeginCreateBreakpoint()
    {
        CreatingBreakpoint = true;
    }

    [ObservableProperty]
    private long? _cyclesValue;

    [ObservableProperty]
    private long? _executionAddressValue;

    [ObservableProperty]
    private long? _memoryAddressValue;

    [ObservableProperty]
    private long? _ioPortNumber;

    [ObservableProperty]
    private long? _interruptNumber;

    [ObservableProperty]
    private BreakPointType _selectedMemoryBreakpointType = BreakPointType.MEMORY_ACCESS;

    public BreakPointType[] MemoryBreakpointTypes => new[] {
        BreakPointType.MEMORY_ACCESS, BreakPointType.MEMORY_WRITE, BreakPointType.MEMORY_READ
    };

    [RelayCommand]
    private void ConfirmBreakpointCreation()
    {
        if (IsExecutionBreakpointSelected)
        {
            if (ExecutionAddressValue is null)
            {
                return;
            }
            BreakpointViewModel executionVm = AddAddressBreakpoint(
                ExecutionAddressValue.Value,
                BreakPointType.CPU_EXECUTION_ADDRESS,
                false,
                () =>
                {
                    PauseAndReportAddress(ExecutionAddressValue.Value);
                }, "Execution breakpoint");
            BreakpointCreated?.Invoke(executionVm);
        }
        else if (IsMemoryBreakpointSelected)
        {
            if (MemoryAddressValue is null)
            {
                return;
            }
            BreakpointViewModel memoryVm = AddAddressBreakpoint(
                MemoryAddressValue.Value,
                SelectedMemoryBreakpointType,
                false,
                () =>
                {
                    PauseAndReportAddress(MemoryAddressValue.Value);
                }, "Memory breakpoint");
            BreakpointCreated?.Invoke(memoryVm);
        }
        else if (IsCyclesBreakpointSelected)
        {
            if (CyclesValue is null)
            {
                return;
            }
            long cyclesValue = CyclesValue.Value;
            BreakpointViewModel cyclesVm = AddAddressBreakpoint(
                cyclesValue,
                BreakPointType.CPU_CYCLES,
                false,
                () =>
                {
                    PauseAndReportCycles(CyclesValue.Value);
                }, "Cycles breakpoint");
            BreakpointCreated?.Invoke(cyclesVm);
        }
        else if (IsInterruptBreakpointSelected)
        {
            if (InterruptNumber is null) {
                return;
            }
            BreakpointViewModel interruptVm = AddAddressBreakpoint(
                InterruptNumber.Value,
                BreakPointType.CPU_INTERRUPT,
                false,
                () =>
                {
                    PauseAndReportInterrupt(InterruptNumber.Value);
                }, "Interrupt breakpoint");
            BreakpointCreated?.Invoke(interruptVm);
        }
        else if (IsIoPortBreakpointSelected)
        {
            if (IoPortNumber is null)
            {
                return;
            }
            BreakpointViewModel ioPortVm = AddAddressBreakpoint(
                IoPortNumber.Value,
                BreakPointType.IO_ACCESS,
                false,
                () =>
                {
                    PauseAndReportIoPort(IoPortNumber.Value);
                }, "I/O Port breakpoint");
            BreakpointCreated?.Invoke(ioPortVm);
        }
        CreatingBreakpoint = false;
    }

    private void PauseAndReportAddress(long address)
    {
        string message = $"Execution breakpoint was reached at address 0x{address:X2}.";
        Pause(message);
    }

    private void PauseAndReportCycles(long cycles)
    {
        string message = $"Cycles breakpoint was reached at {cycles} cycles.";
        Pause(message);
    }

    private void PauseAndReportInterrupt(long interruptNumber)
    {
        string message = $"Interrupt breakpoint was reached at interrupt 0x{interruptNumber:X2}.";
        Pause(message);
    }

    private void PauseAndReportIoPort(long ioPortAddress)
    {
        string message = $"I/O Port breakpoint was reached at 0x{ioPortAddress:X2}.";
        Pause(message);
    }

    private void Pause(string message)
    {
        _pauseHandler.RequestPause(message);
        _uiDispatcher.Post(() =>
        {
            _messenger.Send(new StatusMessage(DateTime.Now, this, message));
        });
    }

    [RelayCommand]
    private void CancelBreakpointCreation()
    {
        CreatingBreakpoint = false;
    }

    public event Action<BreakpointViewModel>? BreakpointDeleted;
    public event Action<BreakpointViewModel>? BreakpointEnabled;
    public event Action<BreakpointViewModel>? BreakpointCreated;
    public event Action<BreakpointViewModel>? BreakpointDisabled;

    [ObservableProperty]
    private ObservableCollection<BreakpointViewModel> _breakpoints = new();

    [RelayCommand(CanExecute = nameof(ToggleSelectedBreakpointCanExecute))]
    private void ToggleSelectedBreakpoint()
    {
        if (SelectedBreakpoint is not null)
        {
            SelectedBreakpoint.Toggle();
            if (SelectedBreakpoint.IsEnabled)
            {
                BreakpointEnabled?.Invoke(SelectedBreakpoint);
            }
            else
            {
                BreakpointDisabled?.Invoke(SelectedBreakpoint);
            }
        }
    }

    private bool ToggleSelectedBreakpointCanExecute() => SelectedBreakpoint is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveBreakpointCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleSelectedBreakpointCommand))]
    private BreakpointViewModel? _selectedBreakpoint;

    public void AddUnconditionalBreakpoint(Action onReached, bool removedOnTrigger)
    {
        _emulatorBreakpointsManager.ToggleBreakPoint(
            new UnconditionalBreakPoint(
                BreakPointType.CPU_EXECUTION_ADDRESS,
                (_) => onReached(),
                removedOnTrigger), on: true);
    }

    public BreakpointViewModel AddAddressBreakpoint(
            long address,
            BreakPointType type,
            bool isRemovedOnTrigger,
            Action onReached,
            string comment = "")
    {
        var breakpointViewModel = new BreakpointViewModel(
            this,
            _emulatorBreakpointsManager,
            address, type, isRemovedOnTrigger, onReached, comment);
        Breakpoints.Add(breakpointViewModel);
        SelectedBreakpoint = breakpointViewModel;
        return breakpointViewModel;
    }

    public BreakpointViewModel? GetBreakpoint(CpuInstructionInfo instructionInfo)
    {
        return Breakpoints.FirstOrDefault(x => x.IsFor(instructionInfo));
    }

    private bool RemoveBreakpointCanExecute() => SelectedBreakpoint is not null;

    [RelayCommand(CanExecute = nameof(RemoveBreakpointCanExecute))]
    private void RemoveBreakpoint()
    {
        DeleteBreakpoint(SelectedBreakpoint);
    }

    public void RemoveBreakpointInternal(BreakpointViewModel vm)
    {
        DeleteBreakpoint(vm);
    }

    private void DeleteBreakpoint(BreakpointViewModel? breakpoint)
    {
        if (breakpoint is null)
        {
            return;
        }
        breakpoint.Disable();
        Breakpoints.Remove(breakpoint);
        BreakpointDeleted?.Invoke(breakpoint);
    }
}
