namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class ExecutionContextManager : InstructionReplacer, IClearable {
    private readonly ILoggerService _loggerService;
    private readonly CfgNodeFeeder _cfgNodeFeeder;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly bool _useCodeOverride;
    private readonly ExecutionContextReturns _executionContextReturns = new();
    private readonly CpuHeavyLogger? _cpuHeavyLogger;

    public ExecutionContextManager(IMemory memory,
        State state,
        CfgNodeFeeder cfgNodeFeeder,
        InstructionReplacerRegistry replacerRegistry,
        FunctionCatalogue functionCatalogue,
        bool useCodeOverride,
        ILoggerService loggerService,
        CpuHeavyLogger? cpuHeavyLogger) : base(replacerRegistry) {
        _loggerService = loggerService;
        _cfgNodeFeeder = cfgNodeFeeder;
        _memory = memory;
        _state = state;
        _functionCatalogue = functionCatalogue;
        _useCodeOverride = useCodeOverride;
        _cpuHeavyLogger = cpuHeavyLogger;
        // Initial fake but non-null context at init
        CurrentExecutionContext = NewExecutionContext(SegmentedAddress.ZERO);
    }

    private int CurrentDepth {
        get;
        set {
            field = value;
            _loggerService.LoggerPropertyBag.ContextIndex = value;
        }
    }

    /// <summary>
    /// Entry points for the CFG Graph(s)
    /// </summary>
    public Dictionary<SegmentedAddress, ISet<CfgInstruction>> ExecutionContextEntryPoints { get; } = new();

    public ExecutionContext CurrentExecutionContext { get; private set; }

    public void SignalEntry() {
        CurrentExecutionContext = NewExecutionContext(_state.IpSegmentedAddress);
    }

    private ExecutionContext NewExecutionContext(SegmentedAddress entryPoint) {
        return new(entryPoint, CurrentDepth, new(_memory, _state, _functionCatalogue, _useCodeOverride, _loggerService));
    }

    public void SignalNewExecutionContext(SegmentedAddress entryAddress, SegmentedAddress expectedReturnAddress) {
        // Save current execution context to be restored when expectedReturnAddress is reached
        _executionContextReturns.PushContextToRestore(expectedReturnAddress, CurrentExecutionContext);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Context at depth {Depth} will be restored when {Address} is reached", CurrentExecutionContext.Depth, expectedReturnAddress);
        }
        // Create a new one at a higher depth
        CurrentDepth++;
        CurrentExecutionContext = NewExecutionContext(entryAddress);
        RegisterCurrentInstructionAsEntryPoint(entryAddress);
        _cpuHeavyLogger?.LogEnteringContext(CurrentDepth, expectedReturnAddress);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("New execution context created for depth {Depth}. Will be destroyed when {Address} is reached", CurrentDepth, expectedReturnAddress);
        }
    }

    public void RestoreExecutionContextIfNeeded(SegmentedAddress returnAddress) {
        ExecutionContext? previousExecutionContext = _executionContextReturns.TryRestoreContext(returnAddress);
        if (previousExecutionContext != null) {
            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                _loggerService.Verbose(@"Reached {Address}, restoring {Depth}", returnAddress, previousExecutionContext.Depth);
            }
            RestoreExecutionContext(previousExecutionContext, returnAddress);
        }
    }

    private void RestoreExecutionContext(ExecutionContext previousExecutionContext, SegmentedAddress returnAddress) {
        _cpuHeavyLogger?.LogLeavingContext(CurrentDepth, returnAddress);
        // Restore previous execution context and depth
        CurrentExecutionContext = previousExecutionContext;
        CurrentDepth--;
        if (CurrentExecutionContext.Depth != CurrentDepth) {
            _loggerService.Warning(@"Restored {Depth} but Current depth is {CurrentDepth}", CurrentExecutionContext.Depth, CurrentDepth);
        }
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose(@"Execution context restored, depth is {Depth}", CurrentDepth);
        }
    }

    private void RegisterCurrentInstructionAsEntryPoint(SegmentedAddress entryAddress) {
        // Register a new entry point
        CfgInstruction toExecute = _cfgNodeFeeder.GetInstructionFromMemoryAtIp();
        if (!ExecutionContextEntryPoints.TryGetValue(entryAddress, out ISet<CfgInstruction>? nodes)) {
            nodes = new HashSet<CfgInstruction>();
            ExecutionContextEntryPoints.Add(entryAddress, nodes);
        }
        nodes.Add(toExecute);
    }

    public override void ReplaceInstruction(CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        if (ExecutionContextEntryPoints.TryGetValue(newInstruction.Address, out ISet<CfgInstruction>? entriesAtAddress)
            && entriesAtAddress.Remove(oldInstruction)) {
            entriesAtAddress.Add(newInstruction);
        }
        UpdateNodeToExecuteIfStale(CurrentExecutionContext, oldInstruction, newInstruction);
        foreach (ExecutionContext stacked in _executionContextReturns.GetAllContexts()) {
            UpdateNodeToExecuteIfStale(stacked, oldInstruction, newInstruction);
        }
    }

    private static void UpdateNodeToExecuteIfStale(ExecutionContext context,
        CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        if (ReferenceEquals(context.NodeToExecuteNextAccordingToGraph, oldInstruction)) {
            context.NodeToExecuteNextAccordingToGraph = newInstruction;
        }
    }

    /// <inheritdoc />
    public void Clear() {
        _executionContextReturns.Clear();
        _functionCatalogue.Clear();
        ExecutionContextEntryPoints.Clear();
        CurrentDepth = 0;
        CurrentExecutionContext = NewExecutionContext(SegmentedAddress.ZERO);
    }
}