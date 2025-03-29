namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class ExecutionContextManager : InstructionReplacer {
    private readonly ILoggerService _loggerService;
    private readonly CfgNodeFeeder _cfgNodeFeeder;
    private readonly IMemory _memory;
    private readonly State _state;
    private int _currentDepth;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly ExecutionContextReturns _executionContextReturns = new();

    public ExecutionContextManager(IMemory memory,
        State state,
        CfgNodeFeeder cfgNodeFeeder,
        InstructionReplacerRegistry replacerRegistry,
        FunctionCatalogue functionCatalogue,
        ILoggerService loggerService) : base(replacerRegistry) {
        _loggerService = loggerService;
        _cfgNodeFeeder = cfgNodeFeeder;
        _memory = memory;
        _state = state;
        _functionCatalogue = functionCatalogue;
        // Initial fake but non-null context at init
        CurrentExecutionContext = NewExecutionContext(SegmentedAddress.ZERO);
    }

    private int CurrentDepth {
        get => _currentDepth;
        set {
            _currentDepth = value;
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
        return new(entryPoint, CurrentDepth, new(_memory, _state, null, _functionCatalogue, _loggerService));
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
            RestoreExecutionContext(previousExecutionContext);
        }
    }

    private void RestoreExecutionContext(ExecutionContext previousExecutionContext) {
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
        CfgInstruction toExecute = _cfgNodeFeeder.CurrentNodeFromInstructionFeeder;
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
    }
}