namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Serilog.Events;

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
using Spice86.Shared.Interfaces;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

/// <summary>
///     Modern implementation of the disassembly view model with improved performance and usability.
/// </summary>
public partial class ModernDisassemblyViewModel : ViewModelWithErrorDialog, IModernDisassemblyViewModel, IDisposable {
    private readonly BreakpointsViewModel _breakpointsViewModel;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IDictionary<SegmentedAddress, FunctionInformation> _functionsInformation;
    private readonly InstructionsDecoder _instructionsDecoder;
    private readonly ILoggerService _logger;
    private readonly IMemory _memory;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;
    private readonly State _state;

    [ObservableProperty]
    private string? _breakpointAddress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    private uint _currentInstructionAddress;

    [ObservableProperty]
    private AvaloniaDictionary<uint, DebuggerLineViewModel> _debuggerLines = [];

    [ObservableProperty]
    private AvaloniaList<FunctionInfo> _functions = [];

    [ObservableProperty]
    private string _header = "Modern Disassembly";

    // Flag to track if we're doing a batch update
    private bool _isBatchUpdating;

    [ObservableProperty]
    private bool _isFunctionInformationProvided;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewDisassemblyViewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyLineCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepIntoCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOverCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateExecutionBreakpointHereCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveExecutionBreakpointHereCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableBreakpointCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableBreakpointCommand))]
    private bool _isPaused;

    // Flag to prevent recursive updates
    private bool _isUpdatingHighlighting;

    // Track the previous instruction address for highlighting updates
    private uint _previousInstructionAddress;

    [ObservableProperty]
    private RegistersViewModel _registers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    private SegmentedAddress? _segmentedStartAddress;

    [ObservableProperty]
    private DebuggerLineViewModel? _selectedDebuggerLine;

    [ObservableProperty]
    private FunctionInfo? _selectedFunction;

    // Cached sorted view of the debugger lines
    private ObservableCollection<DebuggerLineViewModel>? _sortedDebuggerLinesView;

    // Flag to track if the sorted view needs to be updated
    private bool _sortedViewNeedsUpdate = true;

    public ModernDisassemblyViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory, State state, IDictionary<SegmentedAddress, FunctionInformation> functionsInformation,
        BreakpointsViewModel breakpointsViewModel, IPauseHandler pauseHandler, IUIDispatcher uiDispatcher, IMessenger messenger, ITextClipboard textClipboard, ILoggerService loggerService,
        bool canCloseTab = false) : base(uiDispatcher, textClipboard) {
        _logger = loggerService.WithLogLevel(LogEventLevel.Debug);
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
        CanCloseTab = canCloseTab;
        CurrentInstructionAddress = _state.IpPhysicalAddress;

        // Initialize the registers view model
        _registers = new RegistersViewModel(state);

        EnableEventHandlers();
    }

    /// <summary>
    ///     Gets a sorted view of the debugger lines for UI display.
    /// </summary>
    public ObservableCollection<DebuggerLineViewModel> SortedDebuggerLinesView {
        get {
            if (_sortedDebuggerLinesView == null || _sortedViewNeedsUpdate) {
                // Create or update the sorted view
                if (_sortedDebuggerLinesView == null) {
                    _sortedDebuggerLinesView = [];
                } else {
                    _sortedDebuggerLinesView.Clear();
                }

                // Add all items in sorted order
                foreach (KeyValuePair<uint, DebuggerLineViewModel> item in DebuggerLines.OrderBy(kvp => kvp.Key)) {
                    _sortedDebuggerLinesView.Add(item.Value);
                }

                _sortedViewNeedsUpdate = false;
            }

            return _sortedDebuggerLinesView;
        }
    }

    /// <summary>
    ///     Clean up event handlers when the view model is disposed.
    /// </summary>
    public void Dispose() {
        DisableEventHandlers();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     The physical address of the current instruction. This is updated when the emulator pauses.
    /// </summary>
    public uint CurrentInstructionAddress {
        get => _currentInstructionAddress;
        private set {
            if (value != _currentInstructionAddress) {
                _currentInstructionAddress = value;
                OnPropertyChanged();
            }
        }
    }

    IRegistersViewModel IModernDisassemblyViewModel.Registers => Registers;

    /// <summary>
    ///     Gets a debugger line by its address with O(1) lookup time.
    /// </summary>
    /// <param name="address">The address to look up.</param>
    /// <returns>The debugger line if found, otherwise null.</returns>
    public DebuggerLineViewModel? GetLineByAddress(uint address) {
        return DebuggerLines.GetValueOrDefault(address);
    }

    // Explicit interface implementations
    IAsyncRelayCommand IModernDisassemblyViewModel.UpdateDisassemblyCommand => UpdateDisassemblyCommand;
    IRelayCommand IModernDisassemblyViewModel.CopyLineCommand => CopyLineCommand;
    IRelayCommand IModernDisassemblyViewModel.StepIntoCommand => StepIntoCommand;
    IRelayCommand IModernDisassemblyViewModel.StepOverCommand => StepOverCommand;
    IRelayCommand IModernDisassemblyViewModel.GoToFunctionCommand => GoToFunctionCommand;
    IAsyncRelayCommand IModernDisassemblyViewModel.NewDisassemblyViewCommand => NewDisassemblyViewCommand;
    IRelayCommand IModernDisassemblyViewModel.CloseTabCommand => CloseTabCommand;
    IRelayCommand<uint> IModernDisassemblyViewModel.ScrollToAddressCommand => ScrollToAddressCommand;
    IRelayCommand<DebuggerLineViewModel> IModernDisassemblyViewModel.CreateExecutionBreakpointHereCommand => CreateExecutionBreakpointHereCommand;
    IRelayCommand<DebuggerLineViewModel> IModernDisassemblyViewModel.RemoveExecutionBreakpointHereCommand => RemoveExecutionBreakpointHereCommand;
    IRelayCommand<BreakpointViewModel> IModernDisassemblyViewModel.DisableBreakpointCommand => DisableBreakpointCommand;
    IRelayCommand<BreakpointViewModel> IModernDisassemblyViewModel.EnableBreakpointCommand => EnableBreakpointCommand;
    IRelayCommand<DebuggerLineViewModel> IModernDisassemblyViewModel.ToggleBreakpointCommand => ToggleBreakpointCommand;
    IRelayCommand IModernDisassemblyViewModel.MoveCsIpHereCommand => MoveCsIpHereCommand;
    ObservableCollection<DebuggerLineViewModel> IModernDisassemblyViewModel.SortedDebuggerLinesView => SortedDebuggerLinesView;

    /// <summary>
    ///     Updates the debugger lines in batch to avoid multiple collection change notifications.
    /// </summary>
    /// <param name="enrichedInstructions">The dictionary of instructions to add.</param>
    private void UpdateDebuggerLinesInBatch(Dictionary<uint, EnrichedInstruction> enrichedInstructions) {
        try {
            _isBatchUpdating = true;

            // Add all new items at once
            foreach (KeyValuePair<uint, EnrichedInstruction> item in enrichedInstructions) {
                DebuggerLines[item.Key] = new DebuggerLineViewModel(item.Value, _state, _breakpointsViewModel);
            }
        } finally {
            _isBatchUpdating = false;

            // Mark the sorted view as needing an update
            _sortedViewNeedsUpdate = true;

            // Manually trigger notifications after the batch update
            OnPropertyChanged(nameof(DebuggerLines));
            OnPropertyChanged(nameof(SortedDebuggerLinesView));
        }
    }

    // Override OnPropertyChanged to track when the dictionary changes
    protected override void OnPropertyChanged(PropertyChangedEventArgs e) {
        base.OnPropertyChanged(e);

        // If the DebuggerLines property changed, and we're not in a batch update,
        // mark the sorted view as needing an update
        if (!_isBatchUpdating && e.PropertyName == nameof(DebuggerLines)) {
            _sortedViewNeedsUpdate = true;
            OnPropertyChanged(nameof(SortedDebuggerLinesView));
        }
    }

    public void EnableEventHandlers() {
        _pauseHandler.Paused += OnPaused;
        _pauseHandler.Resumed += OnResumed;

        // Subscribe to collection changes in the BreakpointsViewModel
        _breakpointsViewModel.Breakpoints.CollectionChanged += Breakpoints_CollectionChanged;
    }

    public void DisableEventHandlers() {
        _pauseHandler.Paused -= OnPaused;
        _pauseHandler.Resumed -= OnResumed;

        // Unsubscribe from collection changes
        _breakpointsViewModel.Breakpoints.CollectionChanged -= Breakpoints_CollectionChanged;
    }

    private void Breakpoints_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(() => Breakpoints_CollectionChanged(sender, e));

            return;
        }

        // Update all debugger lines that might be affected by the change
        foreach (DebuggerLineViewModel line in DebuggerLines.Values) {
            line.UpdateBreakpointFromViewModel();
        }
    }

    private void OnResumed() {
        _uiDispatcher.Post(() => {
            IsPaused = false;
        });
    }

    private void OnPaused() {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(OnPaused);

            return;
        }

        // Capture the current CPU instruction pointer at the moment of pausing
        uint currentInstructionAddress = _state.IpPhysicalAddress;
        _logger.Debug("Pausing: Captured instruction pointer at {CurrentInstructionAddress:X8}", currentInstructionAddress);

        EnsureAddressIsLoaded(currentInstructionAddress);

        // Set the current instruction address to trigger the view to scroll to it
        CurrentInstructionAddress = currentInstructionAddress;

        // Now that we've ensured the instructions are loaded, update the highlighting
        UpdateCurrentInstructionHighlighting();

        // Update the registers view model
        Registers.Update();

        // Set the paused state last to ensure all updates are complete
        IsPaused = true;
    }

    private void EnsureAddressIsLoaded(uint address) {
        // Check if the current instruction address is in our collection
        if (DebuggerLines.ContainsKey(address)) {
            return;
        }
        _logger.Debug("Current address {CurrentInstructionAddress:X8} not found in DebuggerLines, updating disassembly", address);

        // We need to ensure the disassembly is updated synchronously before continuing
        try {
            IsLoading = true;

            // Use Task.Run and Wait to execute the async method synchronously
            // This ensures the instructions are loaded before we continue
            Task.Run(async () => {
                await UpdateDisassembly(address);
            }).Wait();

            _logger.Debug("Disassembly updated, now contains {DebuggerLinesCount} instructions", DebuggerLines.Count);

            // Verify that the current instruction is now in the collection
            if (!DebuggerLines.ContainsKey(address)) {
                _logger.Warning("Current address {CurrentInstructionAddress} still not found in DebuggerLines after update", address);
            }
        } catch (Exception ex) {
            _logger.Error(ex, "Error updating disassembly");
        } finally {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     Updates the highlighting for the current instruction based on the current CPU state.
    /// </summary>
    private void UpdateCurrentInstructionHighlighting() {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(UpdateCurrentInstructionHighlighting);

            return;
        }

        // Skip if we're already updating the highlighting to prevent recursive calls
        if (_isUpdatingHighlighting) {
            return;
        }

        _isUpdatingHighlighting = true;

        try {
            // Log the current state for debugging
            _logger.Debug("Updating highlighting: CurrentInstructionAddress={CurrentInstructionAddress}, Previous={PreviousInstructionAddress}", CurrentInstructionAddress,
                _previousInstructionAddress);
            _logger.Debug("Current CPU IP: {StateIpPhysicalAddress}", _state.IpPhysicalAddress);

            // Only update if we have a valid current instruction address
            if (CurrentInstructionAddress != 0) {
                // If the current address is in our collection, update its highlighting
                if (DebuggerLines.TryGetValue(CurrentInstructionAddress, out DebuggerLineViewModel? currentLine)) {
                    currentLine.UpdateIsCurrentInstruction();
                    _logger.Debug("Updated current line at {CurrentInstructionAddress:X8}", CurrentInstructionAddress);
                } else {
                    _logger.Warning("Current instruction at {CurrentInstructionAddress:X8} is NOT in the DebuggerLines collection!", CurrentInstructionAddress);
                }

                // If the previous address is in our collection, update its highlighting
                if (_previousInstructionAddress != 0 && _previousInstructionAddress != CurrentInstructionAddress &&
                    DebuggerLines.TryGetValue(_previousInstructionAddress, out DebuggerLineViewModel? previousLine)) {
                    previousLine.UpdateIsCurrentInstruction();
                    _logger.Debug("Updated previous line at {PreviousInstructionAddress:X8}", _previousInstructionAddress);
                }

                // Update the previous IP address for the next time
                _previousInstructionAddress = CurrentInstructionAddress;
            }
        } finally {
            _isUpdatingHighlighting = false;
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
        if (!DebuggerLines.TryGetValue(CurrentInstructionAddress, out DebuggerLineViewModel? debuggerLine)) {
            return;
        }

        // Store the current instruction address before stepping
        uint currentAddress = CurrentInstructionAddress;

        // Only step over instructions that return
        if (!debuggerLine.CanBeSteppedOver) {
            _logger.Debug("Setting unconditional breakpoint for step over");

            _breakpointsViewModel.AddUnconditionalBreakpoint(() => {
                // When the breakpoint is hit, pause the emulator
                Pause("Step over unconditional breakpoint was reached");
                _logger.Debug("Step over breakpoint reached. Previous address: {CurrentAddress:X8}, New address: {StateIpPhysicalAddress:X8}", currentAddress, _state.IpPhysicalAddress);
            }, true);

            _logger.Debug("Resuming execution for step over");
            _pauseHandler.Resume();

            return;
        }

        // Calculate the next instruction address
        uint nextInstructionAddress = debuggerLine.NextAddress;

        // Set the breakpoint at the next instruction address
        _breakpointsViewModel.AddAddressBreakpoint(nextInstructionAddress, BreakPointType.CPU_EXECUTION_ADDRESS, true, () => {
            Pause($"Step over execution breakpoint was reached at address {nextInstructionAddress}");
            _logger.Debug("Step over breakpoint reached. Previous address: {CurrentAddress:X8}, New address: {StateIpPhysicalAddress:X8}", currentAddress, _state.IpPhysicalAddress);
        }, "Step over breakpoint");

        _logger.Debug("Resuming execution for step over");
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepInto() {
        _logger.Debug("Setting unconditional breakpoint for step into");

        // Store the current instruction address before stepping
        uint currentAddress = CurrentInstructionAddress;

        _breakpointsViewModel.AddUnconditionalBreakpoint(() => {
            // When the breakpoint is hit, pause the emulator
            Pause("Step into unconditional breakpoint was reached");
            _logger.Debug("Step into breakpoint reached. Previous address: {CurrentAddress:X8}, New address: {StateIpPhysicalAddress:X8}", currentAddress, _state.IpPhysicalAddress);
        }, true);

        _logger.Debug("Resuming execution for step into");
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void ToggleBreakpoint(DebuggerLineViewModel debuggerLine) {
        if (debuggerLine.Breakpoint != null) {
            // If there's already a breakpoint, toggle it
            debuggerLine.Breakpoint.Toggle();
        } else {
            // If there's no breakpoint, add one
            _breakpointsViewModel.AddAddressBreakpoint(debuggerLine.Address, BreakPointType.CPU_EXECUTION_ADDRESS, false, () => {
                PauseAndReportAddress(debuggerLine.Address);
            });

            // The collection changed event will update the debugger line's breakpoint property
        }
    }

    [RelayCommand]
    private async Task NewDisassemblyView() {
        ModernDisassemblyViewModel disassemblyViewModel = new(
            _emulatorBreakpointsManager, _memory, _state, _functionsInformation, _breakpointsViewModel, _pauseHandler, _uiDispatcher, _messenger, _textClipboard, _logger, true) {
            IsPaused = IsPaused
        };
        await Task.Run(() => _messenger.Send(new AddViewModelMessage<ModernDisassemblyViewModel>(disassemblyViewModel)));
    }

    private bool TryParseMemoryAddress(string addressString, out ulong? result) {
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
    private async Task UpdateDisassembly(uint currentInstructionAddress) {
        IsLoading = true;
        Dictionary<uint, EnrichedInstruction> enrichedInstructions = await Task.Run(() => _instructionsDecoder.DecodeInstructionsExtended(currentInstructionAddress, 2048));

        // Use the batch update method instead of updating items individually
        UpdateDebuggerLinesInBatch(enrichedInstructions);

        IsLoading = false;
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task CopyLine() {
        if (SelectedDebuggerLine is not null) {
            await _textClipboard.SetTextAsync(SelectedDebuggerLine.ToString());
        }
    }

    private void PauseAndReportAddress(uint address) {
        // Create the message
        string message = $"Execution breakpoint was reached at address {address:X8}.";

        // Pause the emulator with the message
        Pause(message);

        _logger.Debug("Paused and reported address {Address:X8}", address);
    }

    private void Pause(string message) {
        _pauseHandler.RequestPause(message);
        _uiDispatcher.Post(() => {
            _messenger.Send(new StatusMessage(DateTime.Now, this, message));
        });
    }

    [RelayCommand]
    private void MoveCsIpHere() {
        if (SelectedDebuggerLine is null) {
            return;
        }
        _state.CS = SelectedDebuggerLine.SegmentedAddress.Segment;
        _state.IP = SelectedDebuggerLine.SegmentedAddress.Offset;
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void GoToFunction(object? parameter) {
        if (parameter is FunctionInfo functionInfo) {
            Console.WriteLine($"Go to function: {functionInfo.Name} at address {functionInfo.Address.Linear:X8}");

            EnsureAddressIsLoaded(functionInfo.Address.Linear);
            DebuggerLineViewModel debuggerLine = DebuggerLines[functionInfo.Address.Linear];
            ScrollToAddress(debuggerLine.Address);
            SelectedDebuggerLine = debuggerLine;
        }
    }

    [RelayCommand]
    private void ScrollToAddress(uint address) {
        // Notify the view that we want to scroll to this address
        // This is a command that will be called from the view
        // The actual scrolling logic is handled in the view
        CurrentInstructionAddress = address;
        OnPropertyChanged(nameof(CurrentInstructionAddress));
    }

    /// <summary>
    /// Command to create an execution breakpoint at the selected instruction.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void CreateExecutionBreakpointHere(DebuggerLineViewModel debuggerLine) {
        if (debuggerLine.Breakpoint == null) {
            // Add a new breakpoint
            _breakpointsViewModel.AddAddressBreakpoint(debuggerLine.Address, BreakPointType.CPU_EXECUTION_ADDRESS, false, () => {
                PauseAndReportAddress(debuggerLine.Address);
            });
            // The collection changed event will update the debugger line's breakpoint property
        }
    }

    /// <summary>
    /// Command to remove an execution breakpoint from the selected instruction.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void RemoveExecutionBreakpointHere(DebuggerLineViewModel debuggerLine) {
        if (debuggerLine.Breakpoint != null) {
            _breakpointsViewModel.RemoveBreakpointInternal(debuggerLine.Breakpoint);
            // The collection changed event will update the debugger line's breakpoint property
        }
    }

    /// <summary>
    /// Command to disable a breakpoint.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void DisableBreakpoint(BreakpointViewModel breakpoint) {
        breakpoint.Disable();
    }

    /// <summary>
    /// Command to enable a breakpoint.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void EnableBreakpoint(BreakpointViewModel breakpoint) {
        breakpoint.Enable();
    }

    /// <summary>
    /// Defines a filter for the autocomplete functionality, filtering structures based on the search text and their size.
    /// </summary>
    public AutoCompleteFilterPredicate<object?> FunctionFilter => (search, item) => search != null
        && item is FunctionInfo {Name: not null} functionInformation
        && functionInformation.Name.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Create the text that is displayed in the textbox when a function is selected.
    /// </summary>
    public AutoCompleteSelector<object>? FunctionItemSelector { get; } = (_, item) => ((FunctionInfo)item).Name ?? "Unknown";
}