namespace Spice86.ViewModels;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Infrastructure;
using Spice86.MemoryWrappers;
using Spice86.Messages;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Globalization;

public partial class DisassemblyViewModel : ViewModelWithErrorDialog {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;
    private readonly BreakpointsViewModel _breakpointsViewModel;
    private readonly IDictionary<uint, FunctionInformation> _functionsInformation;
    private readonly InstructionsDecoder _instructionsDecoder;
    private bool _didCsIpGoOutOfCurrentListing = true;

    public DisassemblyViewModel(
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IMemory memory, State state,
        IDictionary<uint, FunctionInformation> functionsInformation,
        BreakpointsViewModel breakpointsViewModel,
        IPauseHandler pauseHandler, IUIDispatcher uiDispatcher,
        IMessenger messenger, ITextClipboard textClipboard, bool canCloseTab = false)
        : base(uiDispatcher, textClipboard) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _functionsInformation = functionsInformation;
        Functions = new(functionsInformation
            .Select(x => new FunctionInfo() {
                Name = x.Value.Name,
                Address = x.Key,
            }).OrderBy(x => x.Address));
        AreFunctionInformationProvided = functionsInformation.Count > 0;
        _breakpointsViewModel = breakpointsViewModel;
        _messenger = messenger;
        _memory = memory;
        _state = state;
        _pauseHandler = pauseHandler;
        _instructionsDecoder = new(memory, state, functionsInformation, breakpointsViewModel);
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Pausing += OnPausing;
        pauseHandler.Resumed += OnResumed;
        CanCloseTab = canCloseTab;
        breakpointsViewModel.BreakpointDeleted += OnBreakPointUpdateFromBreakpointsViewModel;
        breakpointsViewModel.BreakpointDisabled += OnBreakPointUpdateFromBreakpointsViewModel;
        breakpointsViewModel.BreakpointEnabled += OnBreakPointUpdateFromBreakpointsViewModel;
    }

    private void OnBreakPointUpdateFromBreakpointsViewModel(BreakpointViewModel breakpointViewModel) {
        UpdateAssemblyLineIfShown(breakpointViewModel.Address, breakpointViewModel);
    }

    private void OnResumed() {
        _uiDispatcher.Post(() => {
            IsPaused = false;
            Instructions.Clear();
        });
    }

    [ObservableProperty]
    private bool _areFunctionInformationProvided;

    [ObservableProperty]
    private FunctionInfo? _selectedFunction;

    [ObservableProperty]
    private AvaloniaList<FunctionInfo> _functions = new();

    private void OnPausing() {
        _uiDispatcher.Post(() => {
            IsPaused = true;
            if (!_didCsIpGoOutOfCurrentListing) {
                UpdateDisassemblyInternal();
            } else if (GoToCsIpCommand.CanExecute(null)) {
                GoToCsIpCommand.Execute(null);
                _didCsIpGoOutOfCurrentListing = false;
            }
        });
    }

    [ObservableProperty]
    private string _header = "Disassembly View";

    [ObservableProperty]
    private AvaloniaList<CpuInstructionInfo> _instructions = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToCsIpCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewDisassemblyViewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyLineCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepIntoCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOverCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private bool _creatingExecutionBreakpoint;

    [ObservableProperty]
    private string? _breakpointAddress;

    [RelayCommand]
    private void BeginCreateExecutionBreakpoint() {
        CreatingExecutionBreakpoint = true;
        BreakpointAddress = MemoryUtils.ToPhysicalAddress(_state.CS, _state.IP).ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void CancelCreateExecutionBreakpoint() {
        CreatingExecutionBreakpoint = false;
    }

    [RelayCommand]
    private void ConfirmCreateExecutionBreakpoint() {
        CreatingExecutionBreakpoint = false;
        if (!string.IsNullOrWhiteSpace(BreakpointAddress) &&
            TryParseMemoryAddress(BreakpointAddress, out ulong? breakpointAddressValue)) {
            BreakpointViewModel breakpointViewModel = _breakpointsViewModel.AddAddressBreakpoint(
                (uint)breakpointAddressValue.Value,
                BreakPointType.EXECUTION,
                    isRemovedOnTrigger: false,
                    () => PauseAndReportAddress((long)breakpointAddressValue.Value));
            UpdateAssemblyLineIfShown((uint)breakpointAddressValue.Value, breakpointViewModel);
        }
    }

    private void UpdateAssemblyLineIfShown(uint breakpointAddress, BreakpointViewModel breakpointViewModel) {
        CpuInstructionInfo? shownInstructionAtAddress = Instructions.
            FirstOrDefault(x => x.Address == breakpointAddress);
        if (shownInstructionAtAddress is not null) {
            shownInstructionAtAddress.Breakpoint = breakpointViewModel;
        }
    }

    [ObservableProperty]
    private int _numberOfInstructionsShown = 50;

    [ObservableProperty]
    private bool _isUsingLinearAddressing = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    private SegmentedAddress? _segmentedStartAddress;

    private uint? _startAddress;

    public uint? StartAddress {
        get => _startAddress;
        set {
            SetProperty(ref _startAddress, value);
            UpdateDisassemblyCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    private void UpdateHeader(uint? address) {
        Header = address is null ? "" : $"0x{address:X}";
    }

    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() => _messenger.Send(new RemoveViewModelMessage<DisassemblyViewModel>(this));

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepOver() {
        if (SelectedInstruction is null) {
            return;
        }
        long nextInstructionAddressInListing = SelectedInstruction.Address + SelectedInstruction.Length;
        _emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(
           address: nextInstructionAddressInListing,
           breakPointType: BreakPointType.EXECUTION,
           isRemovedOnTrigger: true,
           onReached: (_) => {
                Pause($"Step over execution breakpoint was reached at address {nextInstructionAddressInListing}");
            }), on: true);
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(SelectedInstructionHasBreakpoint))]
    private void DisableBreakpoint() {
        if (SelectedInstruction?.Breakpoint is null) {
            return;
        }
        SelectedInstruction.Breakpoint.Disable();
    }

    [RelayCommand(CanExecute = nameof(SelectedInstructionHasBreakpoint))]
    private void EnableBreakpoint() {
        if (SelectedInstruction?.Breakpoint is null) {
            return;
        }
        SelectedInstruction.Breakpoint.Enable();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepInto() {
        List<CpuInstructionInfo> instructionInfo = _instructionsDecoder.
            DecodeInstructions(_state.IpPhysicalAddress, 1);
        if(instructionInfo.Count != 0 &&
            instructionInfo[0].FlowControl != Iced.Intel.FlowControl.Next) {
            _didCsIpGoOutOfCurrentListing = true;
        }
        _breakpointsViewModel.AddUnconditionalBreakpoint(
            () => {
                Pause("Step into unconditional breakpoint was reached");
            },
            removedOnTrigger: true);
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void NewDisassemblyView() {
        DisassemblyViewModel disassemblyViewModel = new(
            _emulatorBreakpointsManager,
            _memory, _state, _functionsInformation,
            _breakpointsViewModel, 
            _pauseHandler, _uiDispatcher, _messenger,
            _textClipboard, canCloseTab: true) {
            IsPaused = IsPaused
        };
        _messenger.Send(new AddViewModelMessage<DisassemblyViewModel>(disassemblyViewModel));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task GoToCsIp() {
        SegmentedStartAddress = new(_state.CS, _state.IP);
        await GoToAddress(_state.IpPhysicalAddress);
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task GoToFunction(object? parameter) {
        if (parameter is FunctionInfo functionInfo) {
            await GoToAddress(functionInfo.Address);
        }
    }

    private async Task GoToAddress(uint address) {
        StartAddress = address;
        await UpdateDisassembly();
        SelectedInstruction = Instructions.FirstOrDefault();
    }

    private uint? GetStartAddress() {
        return IsUsingLinearAddressing switch {
            true => StartAddress,
            false => SegmentedStartAddress is null
                ? null
                : MemoryUtils.ToPhysicalAddress(SegmentedStartAddress.Value.Segment,
                    SegmentedStartAddress.Value.Offset),
        };
    }

    private bool CanExecuteUpdateDisassembly() {
        return IsPaused && GetStartAddress() is not null;
    }

    [ObservableProperty]
    private bool _isLoading;

    [RelayCommand(CanExecute = nameof(CanExecuteUpdateDisassembly))]
    private async Task UpdateDisassembly() {
        uint? startAddress = GetStartAddress();
        if (startAddress is null) {
            return;
        }
        Instructions.Clear();
        IsLoading = true;
        Instructions.AddRange(await Task.Run(
            () => DecodeCurrentWindowOfInstructions(startAddress.Value)));
        SelectedInstruction = Instructions.FirstOrDefault();
        UpdateHeader(SelectedInstruction?.Address);
        IsLoading = false;
    }

    private List<CpuInstructionInfo> DecodeCurrentWindowOfInstructions(uint startAddress) {
        return
            _instructionsDecoder.DecodeInstructions(
                startAddress,
                NumberOfInstructionsShown);
    }

    private CpuInstructionInfo? _selectedInstruction;

    public CpuInstructionInfo? SelectedInstruction {
        get => _selectedInstruction;
        set {
            if (value is not null) {
                SelectedFunction = Functions.
                    FirstOrDefault(x => x.Address == value.Address);
                OnPropertyChanged(nameof(SelectedFunction));
            }
            _selectedInstruction = value;
            OnPropertyChanged(nameof(SelectedInstruction));
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task CopyLine() {
        if (SelectedInstruction is not null) {
            await _textClipboard.SetTextAsync(SelectedInstruction.StringRepresentation);
        }
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

    private void UpdateDisassemblyInternal() {
        if (UpdateDisassemblyCommand.CanExecute(null)) {
            UpdateDisassemblyCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void MoveCsIpHere() {
        if (SelectedInstruction is null) {
            return;
        }
        _state.CS = SelectedInstruction.SegmentedAddress.Segment;
        _state.IP = SelectedInstruction.SegmentedAddress.Offset;
        UpdateDisassemblyInternal();
    }

    private bool SelectedInstructionHasBreakpoint() =>
        SelectedInstruction?.Breakpoint is not null;

    [RelayCommand(CanExecute = nameof(SelectedInstructionHasBreakpoint))]
    private void RemoveExecutionBreakpointHere() {
        if (SelectedInstruction?.Breakpoint is null) {
            return;
        }
        _breakpointsViewModel.RemoveBreakpointInternal(SelectedInstruction.Breakpoint);
        SelectedInstruction.Breakpoint = _breakpointsViewModel.GetBreakpoint(SelectedInstruction);
    }

    private bool CreateExecutionBreakpointHereCanExecute() =>
        SelectedInstruction?.Breakpoint is not { Type: BreakPointType.EXECUTION };

    [RelayCommand(CanExecute = nameof(CreateExecutionBreakpointHereCanExecute))]
    private void CreateExecutionBreakpointHere() {
        if (SelectedInstruction is null) {
            return;
        }
        uint address = SelectedInstruction.Address;
        BreakpointViewModel breakpointViewModel = _breakpointsViewModel.AddAddressBreakpoint(address,
            BreakPointType.EXECUTION, isRemovedOnTrigger: false, () => {
                PauseAndReportAddress(address);
            });
        UpdateAssemblyLineIfShown(address, breakpointViewModel);
    }
}