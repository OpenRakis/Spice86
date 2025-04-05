namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
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
using System.Diagnostics.CodeAnalysis;

/// <summary>
///     Implementation of the disassembly view model.
/// </summary>
public partial class DisassemblyViewModel : ViewModelWithErrorDialog, IDisassemblyViewModel, IDisposable {
    private readonly BreakpointsViewModel _breakpointsViewModel;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IDictionary<SegmentedAddress, FunctionInformation> _functionsInformation;
    private readonly InstructionsDecoder _instructionsDecoder;
    private readonly ILoggerService _logger;
    private readonly IMemory _memory;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;
    // Flag to track if we're doing a batch update
    private bool _isBatchUpdating;
    // Flag to prevent recursive updates
    private bool _isUpdatingHighlighting;
    // Flag to track if the sorted view needs to be updated
    private bool _sortedViewNeedsUpdate = true;
    // Track the previous instruction address for highlighting updates
    private SegmentedAddress? _previousInstructionAddress;

    [ObservableProperty]
    private string? _breakpointAddress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    private SegmentedAddress? _currentInstructionAddress;

    [ObservableProperty]
    private AvaloniaDictionary<uint, DebuggerLineViewModel> _debuggerLines = [];

    [ObservableProperty]
    private AvaloniaList<FunctionInfo> _functions = [];

    [ObservableProperty]
    private string _header = "Disassembly";

    [ObservableProperty]
    private bool _isFunctionInformationProvided;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepIntoCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOverCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private RegistersViewModel _registers;

    [ObservableProperty]
    private DebuggerLineViewModel? _selectedDebuggerLine;

    [ObservableProperty]
    private FunctionInfo? _selectedFunction;

    // Cached sorted view of the debugger lines
    private ObservableCollection<DebuggerLineViewModel>? _sortedDebuggerLinesView;

    [ObservableProperty]
    private State _state;

    public DisassemblyViewModel(EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory, State state, IDictionary<SegmentedAddress, FunctionInformation> functionsInformation,
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
        _canCloseTab = canCloseTab;
        CurrentInstructionAddress = _state.IpSegmentedAddress;

        // Initialize the registers view model
        _registers = new RegistersViewModel(state);

        EnableEventHandlers();
    }

    /// <summary>
    ///     Gets a sorted view of the debugger lines for UI display.
    /// </summary>
    public ObservableCollection<DebuggerLineViewModel> SortedDebuggerLinesView {
        get {
            if (_sortedDebuggerLinesView != null && !_sortedViewNeedsUpdate) {
                return _sortedDebuggerLinesView;
            }
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

            return _sortedDebuggerLinesView;
        }
    }

    /// <summary>
    ///     The physical address of the current instruction. This is updated when the emulator pauses.
    /// </summary>
    public SegmentedAddress? CurrentInstructionAddress {
        get => _currentInstructionAddress;
        set {
            if (value != _currentInstructionAddress) {
                _currentInstructionAddress = value;
                OnPropertyChanged();
                UpdateHeader(value);
                UpdateCpuInstructionHighlighting();
            }
        }
    }

    IRegistersViewModel IDisassemblyViewModel.Registers => Registers;

    public bool TryGetLineByAddress(uint address, [NotNullWhen(true)] out DebuggerLineViewModel? debuggerLine) {
        return DebuggerLines.TryGetValue(address, out debuggerLine);
    }
    public bool TryGetLineByAddress(SegmentedAddress address, [NotNullWhen(true)] out DebuggerLineViewModel? debuggerLine) {
        return DebuggerLines.TryGetValue(address.Linear, out debuggerLine);
    }

    /// <summary>
    ///     Defines a filter for the autocomplete functionality, filtering functions based on the search text
    /// </summary>
    public AutoCompleteFilterPredicate<object?> FunctionFilter => (search, item) =>
        string.IsNullOrWhiteSpace(search) || item is FunctionInfo {Name: not null} functionInformation && functionInformation.Name.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Create the text that is displayed in the textbox when a function is selected.
    /// </summary>
    public AutoCompleteSelector<object>? FunctionItemSelector { get; } = (_, item) => {
        if (item is not FunctionInfo functionInfo) {
            return "???";
        }

        return functionInfo.Name is null or "unknown" ? $"unknown [{functionInfo.Address}]" : functionInfo.Name;
    };

    /// <summary>
    ///     Clean up event handlers when the view model is disposed.
    /// </summary>
    public void Dispose() {
        DisableEventHandlers();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Updates the debugger lines in batch to avoid multiple collection change notifications.
    /// </summary>
    /// <param name="enrichedInstructions">The dictionary of instructions to add.</param>
    private void UpdateDebuggerLinesInBatch(Dictionary<uint, EnrichedInstruction> enrichedInstructions) {
        try {
            _isBatchUpdating = true;

            // Add all new items at once
            foreach (KeyValuePair<uint, EnrichedInstruction> item in enrichedInstructions) {
                DebuggerLines[item.Key] = new DebuggerLineViewModel(item.Value, State, _breakpointsViewModel);
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

    private void EnableEventHandlers() {
        _pauseHandler.Paused += OnPaused;
        _pauseHandler.Resumed += OnResumed;

        // Subscribe to collection changes in the BreakpointsViewModel
        _breakpointsViewModel.Breakpoints.CollectionChanged += Breakpoints_CollectionChanged;
    }

    private void DisableEventHandlers() {
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
        SegmentedAddress currentInstructionAddress = State.IpSegmentedAddress;
        _logger.Debug("Pausing: Captured instruction pointer at {CurrentInstructionAddress}", currentInstructionAddress);

        DebuggerLineViewModel debuggerLine = EnsureAddressIsLoaded(currentInstructionAddress);
        debuggerLine.ApplyCpuState();

        // Set the current instruction address to trigger the view to scroll to it
        CurrentInstructionAddress = currentInstructionAddress;

        // Update the registers view model
        Registers.Update();

        // Set the paused state last to ensure all updates are complete
        IsPaused = true;
    }

    private DebuggerLineViewModel EnsureAddressIsLoaded(SegmentedAddress address) {
        // Check if the current instruction address is in our collection
        if (TryGetLineByAddress(address, out DebuggerLineViewModel? debuggerLine)) {
            return debuggerLine;
        }
        _logger.Debug("Current address {CurrentInstructionAddress} not found in DebuggerLines, updating disassembly", address);

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
            if (!TryGetLineByAddress(address, out debuggerLine)) {
                throw new InvalidOperationException($"Current address {address} still not found in DebuggerLines after update");
            }
        } finally {
            IsLoading = false;
        }

        return debuggerLine;
    }

    /// <summary>
    ///     Updates the highlighting for the current instruction based on the current CPU state.
    /// </summary>
    private void UpdateCpuInstructionHighlighting() {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess()) {
            Dispatcher.UIThread.Post(UpdateCpuInstructionHighlighting);

            return;
        }

        // Skip if we're already updating the highlighting to prevent recursive calls
        if (_isUpdatingHighlighting) {
            return;
        }

        _isUpdatingHighlighting = true;

        try {
            // Log the current state for debugging
            _logger.Debug("Updating highlighting: CPU instruction address={CpuInstructionAddress}, Previous={PreviousInstructionAddress}", State.IpSegmentedAddress,
                _previousInstructionAddress);

            DebuggerLineViewModel currentLine = EnsureAddressIsLoaded(State.IpSegmentedAddress);
            currentLine.IsCurrentInstruction = true;

            // If the previous address is in our collection, update its highlighting
            if (_previousInstructionAddress.HasValue
                && _previousInstructionAddress != currentLine.SegmentedAddress
                && TryGetLineByAddress(_previousInstructionAddress.Value, out DebuggerLineViewModel? previousLine)) {
                previousLine.IsCurrentInstruction = false;
            }

            // Update the previous IP address for the next time
            _previousInstructionAddress = State.IpSegmentedAddress;
        } finally {
            _isUpdatingHighlighting = false;
        }
    }

    private void UpdateHeader(SegmentedAddress? address) {
        Header = address?.ToString() ?? "Disassembly";
    }

    private void Pause(string message) {
        _pauseHandler.RequestPause(message);
        _uiDispatcher.Post(() => {
            _messenger.Send(new StatusMessage(DateTime.Now, this, message));
        });
    }
}