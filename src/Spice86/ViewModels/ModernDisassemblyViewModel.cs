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
    IRelayCommand IModernDisassemblyViewModel.MoveCsIpHereCommand => MoveCsIpHereCommand;
    ObservableCollection<DebuggerLineViewModel> IModernDisassemblyViewModel.SortedDebuggerLinesView => SortedDebuggerLinesView;

    public IRelayCommand<uint> ScrollToAddressCommand { get; private set; }

    public ModernDisassemblyViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory, State state, IDictionary<SegmentedAddress, FunctionInformation> functionsInformation,
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
        CanCloseTab = canCloseTab;
        CurrentInstructionAddress = _state.IpPhysicalAddress;
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
        Console.WriteLine($"Pausing: Captured instruction pointer at {currentInstructionAddress:X8}");

        // Check if the current instruction address is in our collection
        if (!DebuggerLines.ContainsKey(currentInstructionAddress)) {
            Console.WriteLine($"Current address {currentInstructionAddress:X8} not found in DebuggerLines, updating disassembly");

            // We need to ensure the disassembly is updated synchronously before continuing
            try {
                IsLoading = true;

                // Use Task.Run and Wait to execute the async method synchronously
                // This ensures the instructions are loaded before we continue
                Task.Run(async () => {
                    await UpdateDisassembly(currentInstructionAddress);
                }).Wait();

                Console.WriteLine($"Disassembly updated, now contains {DebuggerLines.Count} instructions");

                // Verify that the current instruction is now in the collection
                if (!DebuggerLines.ContainsKey(currentInstructionAddress)) {
                    Console.WriteLine($"Warning: Current address {currentInstructionAddress:X8} still not found in DebuggerLines after update");
                }
            } catch (Exception ex) {
                Console.WriteLine($"Error updating disassembly: {ex.Message}");
            } finally {
                IsLoading = false;
            }
        }

        // Set the current instruction address to trigger the view to scroll to it
        CurrentInstructionAddress = currentInstructionAddress;

        // Now that we've ensured the instructions are loaded, update the highlighting
        UpdateCurrentInstructionHighlighting();

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
            Console.WriteLine($"Updating highlighting: CurrentInstructionAddress={CurrentInstructionAddress:X8}, Previous={_previousInstructionAddress:X8}");
            Console.WriteLine($"Current CPU IP: {_state.IpPhysicalAddress:X8}");

            // Only update if we have a valid current instruction address
            if (CurrentInstructionAddress != 0) {
                // If the current address is in our collection, update its highlighting
                if (DebuggerLines.TryGetValue(CurrentInstructionAddress, out DebuggerLineViewModel? currentLine)) {
                    currentLine.UpdateIsCurrentInstruction();
                    Console.WriteLine($"Updated current line at {CurrentInstructionAddress:X8}");
                } else {
                    Console.WriteLine($"WARNING: Current instruction at {CurrentInstructionAddress:X8} is NOT in the DebuggerLines collection!");
                }

                // If the previous address is in our collection, update its highlighting
                if (_previousInstructionAddress != 0 && _previousInstructionAddress != CurrentInstructionAddress &&
                    DebuggerLines.TryGetValue(_previousInstructionAddress, out DebuggerLineViewModel? previousLine)) {
                    previousLine.UpdateIsCurrentInstruction();
                    Console.WriteLine($"Updated previous line at {_previousInstructionAddress:X8}");
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
    private void ConfirmCreateExecutionBreakpoint() {
        CreatingExecutionBreakpoint = false;
        if (!string.IsNullOrWhiteSpace(BreakpointAddress) && TryParseMemoryAddress(BreakpointAddress, out ulong? breakpointAddressValue)) {
            BreakpointViewModel breakpointViewModel = _breakpointsViewModel.AddAddressBreakpoint((long)breakpointAddressValue!.Value, BreakPointType.CPU_EXECUTION_ADDRESS, false,
                () => PauseAndReportAddress((uint)breakpointAddressValue.Value));
            AddBreakpointToListing(breakpointViewModel);
        }
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

        // Only step over instructions that return
        if (!debuggerLine.CanBeSteppedOver) {
            StepInto();

            return;
        }

        // Calculate the next instruction address
        uint nextInstructionAddress = debuggerLine.NextAddress;

        // Store the current instruction address before stepping
        uint currentAddress = CurrentInstructionAddress;

        // Set the breakpoint at the next instruction address
        _emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.CPU_EXECUTION_ADDRESS, nextInstructionAddress, onReached: _ => {
            Pause($"Step over execution breakpoint was reached at address {nextInstructionAddress}");

            // Ensure we update the current instruction address from the CPU state
            // This is done in OnPausing, but we log it here for clarity
            Console.WriteLine($"Step over breakpoint reached. Previous address: {currentAddress:X8}, New address: {_state.IpPhysicalAddress:X8}");
        }, isRemovedOnTrigger: true), on: true);

        _pauseHandler.Resume();
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepInto() {
        Console.WriteLine("Setting unconditional breakpoint for step into");

        // Store the current instruction address before stepping
        uint currentAddress = CurrentInstructionAddress;

        _breakpointsViewModel.AddUnconditionalBreakpoint(() => {
            // When the breakpoint is hit, pause the emulator
            Pause("Step into unconditional breakpoint was reached");

            // Ensure we update the current instruction address from the CPU state
            // This is done in OnPausing, but we log it here for clarity
            Console.WriteLine($"Step into breakpoint reached. Previous address: {currentAddress:X8}, New address: {_state.IpPhysicalAddress:X8}");
        }, true);

        Console.WriteLine("Resuming execution for step into");
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

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task NewDisassemblyView() {
        ModernDisassemblyViewModel disassemblyViewModel = new(
            _emulatorBreakpointsManager, _memory, _state, _functionsInformation, _breakpointsViewModel, _pauseHandler, _uiDispatcher, _messenger, _textClipboard, true) {
            IsPaused = IsPaused
        };
        await Task.Run(() => _messenger.Send(new AddViewModelMessage<ModernDisassemblyViewModel>(disassemblyViewModel)));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task CreateExecutionBreakpointHere() {
        if (SelectedDebuggerLine is null) {
            return;
        }

        uint address = SelectedDebuggerLine.Address;
        BreakpointViewModel breakpointViewModel = _breakpointsViewModel.AddAddressBreakpoint(address, BreakPointType.CPU_EXECUTION_ADDRESS, false, () => {
            PauseAndReportAddress(address);
        });
        await Task.Run(() => SelectedDebuggerLine.Breakpoints.Add(breakpointViewModel));
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

        Console.WriteLine($"Paused and reported address {address:X8}");
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