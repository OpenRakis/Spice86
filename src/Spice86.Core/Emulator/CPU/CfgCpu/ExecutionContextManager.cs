namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Linq;

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

    /// <summary>
    /// The node the CPU is about to execute next. Set in <see cref="CfgCpu.ExecuteNext"/> before
    /// dispatch (so breakpoint pauses show the correct node) and updated after dispatch to the
    /// next-to-execute successor (so loop-level pauses also show the next node, not the last one).
    /// This is global across all execution contexts; only one node is ever executing at a time.
    /// </summary>
    public ICfgNode? ExecutingNode { get; set; }

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

    /// <summary>
    /// Registers <paramref name="node"/> as a CFG entry point so it is treated as a generation root by
    /// the graph exporter and the function partitioner. Used to seed emulator-installed hardware
    /// interrupt handlers: these fire on external events with nondeterministic timing and may never be
    /// reached from the program's observed entry points during discovery, yet must still become
    /// generated overrides so generated code can service the interrupt.
    /// </summary>
    /// <param name="node">The handler entry instruction to register.</param>
    public void RegisterEntryPoint(CfgInstruction node) {
        if (!ExecutionContextEntryPoints.TryGetValue(node.Address, out ISet<CfgInstruction>? nodes)) {
            nodes = new HashSet<CfgInstruction>();
            ExecutionContextEntryPoints.Add(node.Address, nodes);
        }
        nodes.Add(node);
    }

    /// <summary>
    /// Seeds the given known-safe handler entry addresses for speculative exploration and registers
    /// each decoded handler entry node as a CFG entry point. No-op per handler when speculative
    /// exploration is disabled (no seeded node is produced).
    /// </summary>
    /// <param name="handlerAddresses">Entry addresses of emulator-installed interrupt handlers.</param>
    public void SeedKnownSafeHandlersAndRegisterEntryPoints(IReadOnlyList<SegmentedAddress> handlerAddresses) {
        _cfgNodeFeeder.SeedKnownSafeHandlers(handlerAddresses);
        foreach (CfgInstruction entryNode in handlerAddresses
            .Select(handlerAddress => _cfgNodeFeeder.NodeIndex.GetAtAddress(handlerAddress)
                .FirstOrDefault(node => node.ContainingBlock is not null))
            .OfType<CfgInstruction>()) {
            RegisterEntryPoint(entryNode);
        }
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

    /// <summary>
    /// Handles removal fan-out: drops <paramref name="instruction"/> from the
    /// entry-point set at its address so a removed generation root cannot linger as a
    /// detached, de-indexed dead root. Removes the address key when its set empties.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT clear a context's <see cref="ExecutionContext.NodeToExecuteNextAccordingToGraph"/>
    /// that still points at the removed node, unlike <see cref="ReplaceInstruction"/> which repoints it.
    /// Replacement has a live successor to point at; removal does not. The feeder's
    /// reconcile-with-memory path relies on the stale pointer surviving the sweep: a non-live graph
    /// node whose address still matches memory but is no longer indexed is how it detects a swept
    /// speculative node and routes to the live memory node. Nulling it here makes that node null and
    /// trips the address-mismatch guard instead.
    /// </remarks>
    public override void RemoveInstruction(CfgInstruction instruction) {
        DictionaryUtils.RemoveFromCollection(ExecutionContextEntryPoints, instruction.Address, instruction);
    }

    private static void UpdateNodeToExecuteIfStale(ExecutionContext context,
        CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        if (oldInstruction.Equals(context.NodeToExecuteNextAccordingToGraph)) {
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
        ExecutingNode = null;
    }
}