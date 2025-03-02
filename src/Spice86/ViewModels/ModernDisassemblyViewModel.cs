namespace Spice86.ViewModels;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using FluentAvalonia.Core;

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

/// <summary>
/// Modern implementation of the disassembly view model with improved performance and usability.
/// </summary>
public partial class ModernDisassemblyViewModel : ViewModelWithErrorDialog, IDisposable {
    private readonly BreakpointsViewModel _breakpointsViewModel;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IDictionary<uint, FunctionInformation> _functionsInformation;
    private readonly InstructionsDecoder _instructionsDecoder;
    private readonly IMemory _memory;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;
    private readonly State _state;

    [ObservableProperty]
    private string? _breakpointAddress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    [ObservableProperty]
    private bool _creatingExecutionBreakpoint;

    private uint _currentlyFocusedAddress;

    [ObservableProperty]
    private AvaloniaList<FunctionInfo> _functions = [];

    [ObservableProperty]
    private string _header = "Modern Disassembly";

    [ObservableProperty]
    private AvaloniaDictionary<uint,DebuggerLineViewModel> _debuggerLines = [];

    [ObservableProperty]
    private bool _isFunctionInformationProvided;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToCsIpCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewDisassemblyViewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyLineCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepIntoCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOverCommand))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    private SegmentedAddress? _segmentedStartAddress;

    [ObservableProperty]
    private FunctionInfo? _selectedFunction;

    [ObservableProperty]
    private EnrichedInstruction? _selectedInstruction;

    public ModernDisassemblyViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory, State state, IDictionary<uint, FunctionInformation> functionsInformation,
        BreakpointsViewModel breakpointsViewModel, IPauseHandler pauseHandler, IUIDispatcher uiDispatcher, IMessenger messenger, ITextClipboard textClipboard,
        bool canCloseTab = false) : base(uiDispatcher, textClipboard) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _functionsInformation = functionsInformation;
        Functions = new AvaloniaList<FunctionInfo>(functionsInformation.Select(x => new FunctionInfo {
            Name = x.Value.Name,
            Address = x.Key
        }).OrderBy(x => x.Address));
        IsFunctionInformationProvided = Functions.Count > 0;
        _breakpointsViewModel = breakpointsViewModel;
        _messenger = messenger;
        _memory = memory;
        _state = state;
        _pauseHandler = pauseHandler;
        _instructionsDecoder = new InstructionsDecoder(memory, state, functionsInformation, breakpointsViewModel);
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Pausing += OnPausing;
        pauseHandler.Resumed += OnResumed;
        CanCloseTab = canCloseTab;
        breakpointsViewModel.BreakpointDeleted += OnBreakPointUpdateFromBreakpointsViewModel;
        breakpointsViewModel.BreakpointDisabled += OnBreakPointUpdateFromBreakpointsViewModel;
        breakpointsViewModel.BreakpointEnabled += OnBreakPointUpdateFromBreakpointsViewModel;
    }

    public uint CurrentlyFocusedAddress {
        get => _currentlyFocusedAddress;
        set {
            SetProperty(ref _currentlyFocusedAddress, value);
            UpdateDisassemblyCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Clean up event handlers when the view model is disposed.
    /// </summary>
    public void Dispose() {
        // Unsubscribe from events
        _pauseHandler.Pausing -= OnPausing;
        _pauseHandler.Resumed -= OnResumed;
        // Unsubscribe from breakpoint events
        _breakpointsViewModel.BreakpointDeleted -= OnBreakPointUpdateFromBreakpointsViewModel;
        _breakpointsViewModel.BreakpointDisabled -= OnBreakPointUpdateFromBreakpointsViewModel;
        _breakpointsViewModel.BreakpointEnabled -= OnBreakPointUpdateFromBreakpointsViewModel;
    }

    private void OnBreakPointUpdateFromBreakpointsViewModel(BreakpointViewModel breakpoint) {
        AddBreakpointToListing(breakpoint);
    }

    private void OnResumed() {
        _uiDispatcher.Post(() => {
            IsPaused = false;
            DebuggerLines.Clear();
        });
    }

    private void OnPausing() {
        _uiDispatcher.Post(() => {
            IsPaused = true;

            // Check if the current IP is within our current view
            bool ipInCurrentListing = DebuggerLines.ContainsKey(_state.IpPhysicalAddress);

            // Only rebuild the disassembly if necessary
            if (!ipInCurrentListing) {
                // CS:IP changed to lie outside our current listing, update the disassembly
                CurrentlyFocusedAddress = _state.IpPhysicalAddress;
                UpdateDisassemblyInternal();
            }
        });
    }

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
        if (!string.IsNullOrWhiteSpace(BreakpointAddress) && TryParseMemoryAddress(BreakpointAddress, out ulong? breakpointAddressValue)) {
            BreakpointViewModel breakpointViewModel = _breakpointsViewModel.AddAddressBreakpoint((uint)breakpointAddressValue!.Value, BreakPointType.CPU_EXECUTION_ADDRESS, false,
                () => PauseAndReportAddress((long)breakpointAddressValue.Value));
            AddBreakpointToListing(breakpointViewModel);
        }
    }

    private void AddBreakpointToListing(BreakpointViewModel breakpoint) {
        if (DebuggerLines.TryGetValue(breakpoint.Address, out DebuggerLineViewModel? debuggerLine)) {
            debuggerLine.Breakpoints.Add(breakpoint);
        }
    }

    private void UpdateHeader(uint? address) {
        Header = address is null ? "Modern Disassembly" : $"Modern 0x{address:X}";
    }

    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() {
        _messenger.Send(new RemoveViewModelMessage<ModernDisassemblyViewModel>(this));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepOver() {
        if (!DebuggerLines.TryGetValue(_state.IpPhysicalAddress, out DebuggerLineViewModel? debuggerLine)) {
            return;
        }
        // Only step over instructions that return
        if (!debuggerLine.CanBeSteppedOver) {
            StepInto();

            return;
        }

        // Calculate the next instruction address
        uint nextInstructionAddress = debuggerLine.NextAddress;

        // Set the breakpoint at the next instruction address
        _emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_EXECUTION_ADDRESS, nextInstructionAddress, onReached: _ => {
            Pause($"Step over execution breakpoint was reached at address {nextInstructionAddress}");
        }, isRemovedOnTrigger: true), on: true);
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(SelectedInstructionHasBreakpoint))]
    private void DisableBreakpoint() {
        SelectedInstruction?.Breakpoints.ForEach(bp => bp.Disable());
    }

    [RelayCommand(CanExecute = nameof(SelectedInstructionHasBreakpoint))]
    private void EnableBreakpoint() {
        SelectedInstruction?.Breakpoints.ForEach(bp => bp.Enable());
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepInto() {
        _breakpointsViewModel.AddUnconditionalBreakpoint(() => {
            Pause("Step into unconditional breakpoint was reached");
        }, true);
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void NewDisassemblyView() {
        ModernDisassemblyViewModel disassemblyViewModel = new(
            _emulatorBreakpointsManager, _memory, _state, _functionsInformation, _breakpointsViewModel, _pauseHandler, _uiDispatcher, _messenger, _textClipboard, true) {
            IsPaused = IsPaused
        };
        _messenger.Send(new AddViewModelMessage<ModernDisassemblyViewModel>(disassemblyViewModel));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task GoToCsIp() {
        await GoToAddress(_state.IpPhysicalAddress);
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task GoToFunction(object? parameter) {
        if (parameter is FunctionInfo functionInfo) {
            await GoToAddress(functionInfo.Address);
        }
    }

    private async Task GoToAddress(uint address) {
        CurrentlyFocusedAddress = address;
        await UpdateDisassembly();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task UpdateDisassembly() {
        DebuggerLines.Clear();
        IsLoading = true;
        Dictionary<uint,EnrichedInstruction> enrichedInstructions = await Task.Run(() => DecodeCurrentWindowOfInstructions(CurrentlyFocusedAddress));
        foreach ((uint key, EnrichedInstruction value) in enrichedInstructions) {
            DebuggerLines[key] = new DebuggerLineViewModel(value, _state);
        }
        UpdateHeader(DebuggerLines.FirstOrDefault().Key);
        IsLoading = false;
    }

    private Dictionary<uint, EnrichedInstruction> DecodeCurrentWindowOfInstructions(uint centerAddress) {
        return _instructionsDecoder.DecodeInstructionsExtended(centerAddress, 2048);
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task CopyLine() {
        if (SelectedInstruction is not null) {
            await _textClipboard.SetTextAsync(SelectedInstruction.ToString());
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

    private bool SelectedInstructionHasBreakpoint() {
        return SelectedInstruction?.Breakpoints.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(SelectedInstructionHasBreakpoint))]
    private void RemoveExecutionBreakpointHere() {
        if (SelectedInstruction == null || SelectedInstruction.Breakpoints.Count == 0) {
            return;
        }
        foreach (BreakpointViewModel breakpoint in SelectedInstruction.Breakpoints) {
            _breakpointsViewModel.RemoveBreakpointInternal(breakpoint);
            SelectedInstruction.Breakpoints.Remove(breakpoint);
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void CreateExecutionBreakpointHere() {
        if (SelectedInstruction is null) {
            return;
        }
        uint address = SelectedInstruction.Instruction.IP32;
        BreakpointViewModel breakpointViewModel = _breakpointsViewModel.AddAddressBreakpoint(address, BreakPointType.CPU_EXECUTION_ADDRESS, false, () => {
            PauseAndReportAddress(address);
        });
        SelectedInstruction.Breakpoints.Add(breakpointViewModel);
    }

    private new bool TryParseMemoryAddress(string addressString, out ulong? result) {
        result = null;
        if (string.IsNullOrWhiteSpace(addressString)) {
            return false;
        }

        // Try to parse as a hexadecimal number
        if (addressString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            addressString = addressString[2..];
        }

        if (ulong.TryParse(addressString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong address)) {
            result = address;

            return true;
        }

        return false;
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void OpenClassicView() {
        DisassemblyViewModel classicViewModel = new(
            _emulatorBreakpointsManager, _memory, _state, _functionsInformation, _breakpointsViewModel, _pauseHandler, _uiDispatcher, _messenger, _textClipboard, true) {
            IsPaused = IsPaused
        };

        // If we have a selected instruction, set the same address in the classic view
        if (SelectedInstruction != null) {
            classicViewModel.StartAddress = SelectedInstruction.Instruction.IP32;
            classicViewModel.UpdateDisassemblyCommand.Execute(null);
        }

        _messenger.Send(new AddViewModelMessage<DisassemblyViewModel>(classicViewModel));
    }

    /// <summary>
    /// Scrolls the view by the specified number of instructions.
    /// </summary>
    /// <param name="instructionOffset">The number of instructions to scroll (positive = down, negative = up)</param>
    [RelayCommand]
    private async Task ScrollView(int instructionOffset) {
        if (DebuggerLines.Count == 0 || !IsPaused) {
            return;
        }

        // Find the index of the current center instruction
        int currentIndex = DebuggerLines.Keys.IndexOf(CurrentlyFocusedAddress);

        // Calculate the new center index
        int newIndex = Math.Clamp(currentIndex + instructionOffset, 0, DebuggerLines.Count - 1);
        uint newCenterAddress = (uint)DebuggerLines.Keys.ElementAt(newIndex);

        // If we're at the edge of our loaded instructions, we jump to the address, which will update the disassembly
        if (newIndex < DebuggerLines.Count / 4 || newIndex > DebuggerLines.Count * 3 / 4) {
            // Get the address to center on
            await GoToAddress(newCenterAddress);
        } else {
            // Just update the selected instruction
            CurrentlyFocusedAddress = newCenterAddress;

            // Notify the view to scroll to this instruction
            ScrollToAddress?.Invoke(newCenterAddress);
        }
    }

    /// <summary>
    /// Event that signals the view to scroll to a specific address.
    /// </summary>
    public event Action<uint>? ScrollToAddress;

    /// <summary>
    /// Scrolls the view up by one page.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task PageUp() {
        await ScrollView(-20);
    }

    /// <summary>
    /// Scrolls the view down by one page.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task PageDown() {
        await ScrollView(20);
    }

    /// <summary>
    /// Scrolls the view up by one instruction.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task LineUp() {
        await ScrollView(-1);
    }

    /// <summary>
    /// Scrolls the view down by one instruction.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task LineDown() {
        await ScrollView(1);
    }
}