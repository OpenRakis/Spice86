namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Threading;

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
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

/// <summary>
/// Modern implementation of the disassembly view model with improved performance and usability.
/// </summary>
public partial class ModernDisassemblyViewModel : ViewModelWithErrorDialog, IModernDisassemblyViewModel, IDisposable {
    private readonly BreakpointsViewModel _breakpointsViewModel;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IDictionary<SegmentedAddress, FunctionInformation> _functionsInformation;
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

    [ObservableProperty]
    private AvaloniaList<FunctionInfo> _functions = [];

    [ObservableProperty]
    private string _header = "Modern Disassembly";

    [ObservableProperty]
    private AvaloniaDictionary<uint, DebuggerLineViewModel> _debuggerLines = [];

    // Flag to track if we're doing a batch update
    private bool _isBatchUpdating;

    // Cached sorted view of the debugger lines
    private ObservableCollection<DebuggerLineViewModel>? _sortedDebuggerLinesView;

    // Flag to track if the sorted view needs to be updated
    private bool _sortedViewNeedsUpdate = true;

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
    private bool _isPaused;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    private SegmentedAddress? _segmentedStartAddress;

    [ObservableProperty]
    private FunctionInfo? _selectedFunction;

    [ObservableProperty]
    private DebuggerLineViewModel? _selectedDebuggerLine;

    private uint _currentInstructionAddress;

    /// <summary>
    /// The physical address of the current instruction. This is updated when the emulator pauses.
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

    // Track the previous instruction address for highlighting updates
    private uint _previousInstructionAddress;

    // Flag to prevent recursive updates
    private bool _isUpdatingHighlighting;
    private readonly ILoggerService _logger;

    [ObservableProperty]
    private RegistersViewModel _registers;

    IRegistersViewModel IModernDisassemblyViewModel.Registers => Registers;

    /// <summary>
    /// Gets a sorted view of the debugger lines for UI display.
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
    /// Gets a debugger line by its address with O(1) lookup time.
    /// </summary>
    /// <param name="address">The address to look up.</param>
    /// <returns>The debugger line if found, otherwise null.</returns>
    public DebuggerLineViewModel? GetLineByAddress(uint address) {
        return DebuggerLines.GetValueOrDefault(address);
    }

    /// <summary>
    /// Updates the debugger lines in batch to avoid multiple collection change notifications.
    /// </summary>
    /// <param name="enrichedInstructions">The dictionary of instructions to add.</param>
    private void UpdateDebuggerLinesInBatch(Dictionary<uint, EnrichedInstruction> enrichedInstructions) {
        try {
            _isBatchUpdating = true;

            // Add all new items at once
            foreach (KeyValuePair<uint, EnrichedInstruction> item in enrichedInstructions) {
                DebuggerLines[item.Key] = new DebuggerLineViewModel(item.Value, _state);
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

    // Explicit interface implementations
    IAsyncRelayCommand IModernDisassemblyViewModel.UpdateDisassemblyCommand => UpdateDisassemblyCommand;
    IRelayCommand IModernDisassemblyViewModel.CopyLineCommand => CopyLineCommand;
    IRelayCommand IModernDisassemblyViewModel.StepIntoCommand => StepIntoCommand;
    IRelayCommand IModernDisassemblyViewModel.StepOverCommand => StepOverCommand;
    IRelayCommand IModernDisassemblyViewModel.GoToFunctionCommand => GoToFunctionCommand;
    IAsyncRelayCommand IModernDisassemblyViewModel.NewDisassemblyViewCommand => NewDisassemblyViewCommand;
    IRelayCommand IModernDisassemblyViewModel.CloseTabCommand => CloseTabCommand;
    IRelayCommand<uint> IModernDisassemblyViewModel.ScrollToAddressCommand => ScrollToAddressCommand;
    IAsyncRelayCommand IModernDisassemblyViewModel.CreateExecutionBreakpointHereCommand => CreateExecutionBreakpointHereCommand;
    IRelayCommand IModernDisassemblyViewModel.RemoveExecutionBreakpointHereCommand => RemoveExecutionBreakpointHereCommand;
    IRelayCommand IModernDisassemblyViewModel.DisableBreakpointCommand => DisableBreakpointCommand;
    IRelayCommand IModernDisassemblyViewModel.EnableBreakpointCommand => EnableBreakpointCommand;
    IRelayCommand IModernDisassemblyViewModel.ToggleBreakpointCommand => ToggleBreakpointCommand;
    IRelayCommand IModernDisassemblyViewModel.MoveCsIpHereCommand => MoveCsIpHereCommand;
    ObservableCollection<DebuggerLineViewModel> IModernDisassemblyViewModel.SortedDebuggerLinesView => SortedDebuggerLinesView;

    public IRelayCommand<uint> ScrollToAddressCommand { get; private set; }

    public ModernDisassemblyViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory, State state, IDictionary<SegmentedAddress, FunctionInformation> functionsInformation,
        BreakpointsViewModel breakpointsViewModel, IPauseHandler pauseHandler, IUIDispatcher uiDispatcher, IMessenger messenger, ITextClipboard textClipboard, ILoggerService loggerService,
        bool canCloseTab = false) : base(uiDispatcher, textClipboard) {
        _logger = loggerService;
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
        ScrollToAddressCommand = new RelayCommand<uint>(ScrollToAddress);
    }

    public void EnableEventHandlers() {
        _pauseHandler.Paused += OnPaused;
        _pauseHandler.Resumed += OnResumed;
        _breakpointsViewModel.BreakpointDeleted += OnBreakPointUpdateFromBreakpointsViewModel;
        _breakpointsViewModel.BreakpointDisabled += OnBreakPointUpdateFromBreakpointsViewModel;
        _breakpointsViewModel.BreakpointEnabled += OnBreakPointUpdateFromBreakpointsViewModel;
    }

    public void DisableEventHandlers() {
        _pauseHandler.Paused -= OnPaused;
        _pauseHandler.Resumed -= OnResumed;
        _breakpointsViewModel.BreakpointDeleted -= OnBreakPointUpdateFromBreakpointsViewModel;
        _breakpointsViewModel.BreakpointDisabled -= OnBreakPointUpdateFromBreakpointsViewModel;
        _breakpointsViewModel.BreakpointEnabled -= OnBreakPointUpdateFromBreakpointsViewModel;
    }

    /// <summary>
    /// Clean up event handlers when the view model is disposed.
    /// </summary>
    public void Dispose() {
        DisableEventHandlers();
        GC.SuppressFinalize(this);
    }

    private void OnBreakPointUpdateFromBreakpointsViewModel(BreakpointViewModel breakpoint) {
        // todo: show/hide breakpint on current line
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

        // Check if the current instruction address is in our collection
        if (!DebuggerLines.ContainsKey(currentInstructionAddress)) {
            _logger.Debug("Current address {CurrentInstructionAddress:X8} not found in DebuggerLines, updating disassembly", currentInstructionAddress);

            // We need to ensure the disassembly is updated synchronously before continuing
            try {
                IsLoading = true;

                // Use Task.Run and Wait to execute the async method synchronously
                // This ensures the instructions are loaded before we continue
                Task.Run(async () => {
                    await UpdateDisassembly(currentInstructionAddress);
                }).Wait();

                _logger.Debug("Disassembly updated, now contains {DebuggerLinesCount} instructions", DebuggerLines.Count);

                // Verify that the current instruction is now in the collection
                if (!DebuggerLines.ContainsKey(currentInstructionAddress)) {
                    _logger.Warning("Current address {CurrentInstructionAddress} still not found in DebuggerLines after update", currentInstructionAddress);
                }
            } catch (Exception ex) {
                _logger.Error(ex, "Error updating disassembly");
            } finally {
                IsLoading = false;
            }
        }

        // Set the current instruction address to trigger the view to scroll to it
        CurrentInstructionAddress = currentInstructionAddress;

        // Now that we've ensured the instructions are loaded, update the highlighting
        UpdateCurrentInstructionHighlighting();

        // Update the registers view model
        Registers.Update();

        // Set the paused state last to ensure all updates are complete
        IsPaused = true;
    }

    /// <summary>
    /// Updates the highlighting for the current instruction based on the current CPU state.
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
    private async Task ConfirmCreateExecutionBreakpoint() {
        CreatingExecutionBreakpoint = false;
        if (!string.IsNullOrWhiteSpace(BreakpointAddress) && TryParseMemoryAddress(BreakpointAddress, out ulong? breakpointAddressValue)) {
            BreakpointViewModel breakpointViewModel = _breakpointsViewModel.AddAddressBreakpoint((uint)breakpointAddressValue!.Value, BreakPointType.CPU_EXECUTION_ADDRESS, false,
                () => {
                    PauseAndReportAddress((uint)breakpointAddressValue.Value);
                });
            
            // Use await to make this method truly async
            await Task.CompletedTask;
        }
        BreakpointAddress = string.Empty;
    }

    private void AddBreakpointToListing(BreakpointViewModel breakpoint) {
        if (DebuggerLines.TryGetValue((uint)breakpoint.Address, out DebuggerLineViewModel? debuggerLine)) {
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
        _emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_EXECUTION_ADDRESS, nextInstructionAddress, onReached: _ => {
            Pause($"Step over execution breakpoint was reached at address {nextInstructionAddress}");
            _logger.Debug("Step over breakpoint reached. Previous address: {CurrentAddress:X8}, New address: {StateIpPhysicalAddress:X8}", currentAddress, _state.IpPhysicalAddress);
        }, isRemovedOnTrigger: true), on: true);

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

            // Ensure we update the current instruction address from the CPU state
            // This is done in OnPausing, but we log it here for clarity
            _logger.Debug("Step into breakpoint reached. Previous address: {CurrentAddress:X8}, New address: {StateIpPhysicalAddress:X8}", currentAddress, _state.IpPhysicalAddress);
        }, true);

        _logger.Debug("Resuming execution for step into");
        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(SelectedDebuggerLineHasBreakpoint))]
    private void DisableBreakpoint() {
        SelectedDebuggerLine?.Breakpoints.ForEach(bp => bp.Disable());
    }

    [RelayCommand(CanExecute = nameof(SelectedDebuggerLineHasBreakpoint))]
    private void EnableBreakpoint() {
        SelectedDebuggerLine?.Breakpoints.ForEach(bp => bp.Enable());
    }

    [RelayCommand]
    public void ToggleBreakpoint(DebuggerLineViewModel line)
    {
        if (line == null)
        {
            return;
        }

        // Select the line
        SelectedDebuggerLine = line;

        if (line.HasBreakpoint)
        {
            // If breakpoints exist, remove them
            Console.WriteLine($"Removing breakpoint at address: {line.Address:X8}");
            _logger.Debug($"Removing breakpoint at address: {line.Address:X8}");
            
            // Remove all breakpoints for this line
            foreach (BreakpointViewModel breakpoint in line.Breakpoints.ToList())
            {
                _breakpointsViewModel.RemoveBreakpointInternal(breakpoint);
            }
            
            // Clear the breakpoints collection
            line.Breakpoints.Clear();
            
            // Force UI refresh
            _uiDispatcher.Post(() => {
                OnPropertyChanged(nameof(SelectedDebuggerLine));
            });
        }
        else
        {
            // If no breakpoints exist, create one
            if (IsPaused)
            {
                CreateExecutionBreakpointHereCommand.Execute(null);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task NewDisassemblyView() {
        ModernDisassemblyViewModel disassemblyViewModel = new(
            _emulatorBreakpointsManager, _memory, _state, _functionsInformation, _breakpointsViewModel, _pauseHandler, _uiDispatcher, _messenger, _textClipboard, _logger, true) {
            IsPaused = IsPaused
        };
        await Task.Run(() => _messenger.Send(new AddViewModelMessage<ModernDisassemblyViewModel>(disassemblyViewModel)));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task CreateExecutionBreakpointHere() {
        if (SelectedDebuggerLine == null) {
            return;
        }
        _logger.Debug($"Creating breakpoint at address: {SelectedDebuggerLine.Address:X8}");
        
        // Check if a breakpoint already exists at this address
        if (SelectedDebuggerLine.HasBreakpoint) {
            _logger.Debug($"Breakpoint already exists at address: {SelectedDebuggerLine.Address:X8}");
            return;
        }

        uint address = (uint)SelectedDebuggerLine.Address;
        BreakpointViewModel breakpointViewModel = _breakpointsViewModel.AddAddressBreakpoint(address, BreakPointType.CPU_EXECUTION_ADDRESS, false, () => {
            PauseAndReportAddress(address);
        });

        _logger.Debug($"Breakpoint created successfully, Enabled: {breakpointViewModel.IsEnabled}");
        
        // Add the breakpoint to the line's collection and update UI immediately
        SelectedDebuggerLine.Breakpoints.Add(breakpointViewModel);
        
        // Force UI refresh using await to make this method truly async
        await _uiDispatcher.InvokeAsync(() => {
            OnPropertyChanged(nameof(SelectedDebuggerLine));
            
            // Force the UI to refresh by triggering property changed events
            DebuggerLineViewModel currentLine = SelectedDebuggerLine;
            SelectedDebuggerLine = null;
            SelectedDebuggerLine = currentLine;
        });
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

    private async Task GoToAddress(uint address) {
        await UpdateDisassembly(address);
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

    private bool SelectedDebuggerLineHasBreakpoint() {
        return SelectedDebuggerLine?.Breakpoints.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(SelectedDebuggerLineHasBreakpoint))]
    private void RemoveExecutionBreakpointHere() {
        if (SelectedDebuggerLine == null || SelectedDebuggerLine.Breakpoints.Count == 0) {
            return;
        }

        // Create a copy of the breakpoints collection to avoid modifying while iterating
        List<BreakpointViewModel> breakpointsToRemove = SelectedDebuggerLine.Breakpoints.ToList();

        foreach (BreakpointViewModel breakpoint in breakpointsToRemove) {
            _breakpointsViewModel.RemoveBreakpointInternal(breakpoint);
            SelectedDebuggerLine.Breakpoints.Remove(breakpoint);
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task GoToFunction(object? parameter) {
        if (parameter is FunctionInfo functionInfo) {
            await GoToAddress(functionInfo.Address.Linear);
        }
    }

    private void ScrollToAddress(uint address) {
        // Notify the view that we want to scroll to this address
        // This is a command that will be called from the view
        // The actual scrolling logic is handled in the view
        CurrentInstructionAddress = address;
        OnPropertyChanged(nameof(CurrentInstructionAddress));
    }
}