namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Microsoft.Extensions.Logging;

using Spice86.Logging;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Linq;

public class ExecutionContextManager : InstructionReplacer, IClearable {
    private readonly ILogger _loggerService;
    private readonly Spice86LoggerState _loggerState;
    private readonly CfgNodeFeeder _cfgNodeFeeder;
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly bool _useCodeOverride;
    private readonly ExecutionContextReturns _executionContextReturns = new();
    private readonly CpuHeavyLogger? _cpuHeavyLogger;
    private readonly InstalledInterruptHandlers _installedInterruptHandlers;
    // Addresses of program-installed handlers on machine-invokable vectors that are still waiting
    // for their entry node to be decoded (first decode is pending on untrusted bytes, or the
    // install call came before the bytes held their loaded values).
    private readonly HashSet<SegmentedAddress> _pendingEntryPoints = new();

    // DOS vectors the emulator itself routes to program code, so a handler the program installs on
    // one can be invoked by the machine: terminate (22h, via the patched frame the terminate path
    // writes), break (1Bh) and Ctrl-C (23h), critical error (24h), and the idle call (28h) the
    // int 21h stdin-wait stub raises. Hardware vectors and the chained vectors the installed
    // handlers declare are covered through InstalledInterruptHandlers instead.
    private static readonly HashSet<byte> MachineInvocableDosVectors = [0x1B, 0x22, 0x23, 0x24, 0x28];

    public ExecutionContextManager(IMemory memory,
        State state,
        CfgNodeFeeder cfgNodeFeeder,
        InstructionReplacerRegistry replacerRegistry,
        FunctionCatalogue functionCatalogue,
        InstalledInterruptHandlers installedInterruptHandlers,
        bool useCodeOverride,
        ILogger loggerService,
        Spice86LoggerState loggerState,
        CpuHeavyLogger? cpuHeavyLogger) : base(replacerRegistry) {
        _loggerService = loggerService;
        _loggerState = loggerState;
        _cfgNodeFeeder = cfgNodeFeeder;
        _memory = memory;
        _state = state;
        _functionCatalogue = functionCatalogue;
        _installedInterruptHandlers = installedInterruptHandlers;
        _useCodeOverride = useCodeOverride;
        _cpuHeavyLogger = cpuHeavyLogger;
        // Initial fake but non-null context at init
        CurrentExecutionContext = NewExecutionContext(SegmentedAddress.ZERO);
        // Predecessorless speculative seeds (catalogue entries, handler seeds) only become visible
        // to the block-graph export through the entry-point set.
        _cfgNodeFeeder.SpeculativeSeedCreated += OnSpeculativeSeedCreated;
    }

    private int CurrentDepth {
        get;
        set {
            field = value;
            _loggerState.ContextIndex = value;
        }
    }

    /// <summary>
    /// Entry points for the CFG Graph(s)
    /// </summary>
    public Dictionary<SegmentedAddress, ISet<CfgInstruction>> ExecutionContextEntryPoints { get; } = new();

    /// <summary>The (vector, handler) pair of every hardware interrupt delivered during the run,
    /// plus every handler the program installed on a hardware vector (delivered or not): a program
    /// can re-hook a vector, so a vector's delivery targets are not recoverable from the final IVT
    /// alone.</summary>
    public HashSet<(byte Vector, SegmentedAddress Target)> HardwareInterruptDeliveries { get; } = new();

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
        if (_loggerService.IsEnabled(LogLevel.Trace)) {
            _loggerService.LogTrace("Context at depth {Depth} will be restored when {Address} is reached", CurrentExecutionContext.Depth, expectedReturnAddress);
        }
        // Create a new one at a higher depth
        CurrentDepth++;
        CurrentExecutionContext = NewExecutionContext(entryAddress);
        RegisterCurrentInstructionAsEntryPoint(entryAddress);
        _cpuHeavyLogger?.LogEnteringContext(CurrentDepth, expectedReturnAddress);
        if (_loggerService.IsEnabled(LogLevel.Trace)) {
            _loggerService.LogTrace("New execution context created for depth {Depth}. Will be destroyed when {Address} is reached", CurrentDepth, expectedReturnAddress);
        }
    }

    public void RestoreExecutionContextIfNeeded(SegmentedAddress returnAddress) {
        ExecutionContext? previousExecutionContext = _executionContextReturns.TryRestoreContext(returnAddress);
        if (previousExecutionContext != null) {
            if (_loggerService.IsEnabled(LogLevel.Trace)) {
                _loggerService.LogTrace(@"Reached {Address}, restoring {Depth}", returnAddress, previousExecutionContext.Depth);
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
            _loggerService.LogWarning(@"Restored {Depth} but Current depth is {CurrentDepth}", CurrentExecutionContext.Depth, CurrentDepth);
        }
        if (_loggerService.IsEnabled(LogLevel.Trace)) {
            _loggerService.LogTrace(@"Execution context restored, depth is {Depth}", CurrentDepth);
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

    /// <summary>
    /// Explores an interrupt handler the program just installed through <c>int 21h AH=25h</c> and,
    /// when <paramref name="vector"/> is one the machine can invoke on its own (hardware IRQs,
    /// chained vectors the installed handlers raise, and the DOS vectors the emulator routes to
    /// program code), registers the handler entry node as a generation root once it exists with a
    /// block. Handlers on other vectors are explored but not registered: nothing outside the
    /// program can invoke them. No-op for the exploration itself when speculation is off.
    /// </summary>
    /// <param name="vector">The interrupt vector the handler was installed on.</param>
    /// <param name="handler">The installed handler's entry address.</param>
    /// <param name="trigger">The <c>int 21h</c> node that invoked the install; pending candidates
    /// are keyed on it and retried on each install from the same node.</param>
    public void ExploreInstalledHandler(byte vector, SegmentedAddress handler, CfgInstruction? trigger) {
        if (!IsMachineInvocableVector(vector)) {
            _cfgNodeFeeder.ExploreInstalledHandler(handler, trigger);
            return;
        }
        // Registered before the exploration call: the seed event fires synchronously when the
        // handler decodes and needs this marker to turn the new node into an entry point.
        _pendingEntryPoints.Add(handler);
        _cfgNodeFeeder.ExploreInstalledHandler(handler, trigger);
        TryRegisterPendingEntryPoint(handler);
        if (_installedInterruptHandlers.HardwareVectorNumbers.Contains(vector)) {
            // A hardware vector can deliver to this handler whether or not a delivery was
            // observed; the generated dispatch only emits an arm when the handler entry was
            // registered, so a handler that never decoded keeps failing as untested.
            HardwareInterruptDeliveries.Add((vector, handler));
        }
    }

    private bool IsMachineInvocableVector(byte vector) =>
        _installedInterruptHandlers.HardwareVectorNumbers.Contains(vector)
        || _installedInterruptHandlers.ChainedVectorNumbers.Contains(vector)
        || MachineInvocableDosVectors.Contains(vector);

    private void TryRegisterPendingEntryPoint(SegmentedAddress address) {
        CfgInstruction? node = _cfgNodeFeeder.NodeIndex.GetAtAddress(address)
            .FirstOrDefault(n => n.ContainingBlock is not null);
        if (node is not null) {
            RegisterEntryPoint(node);
            _pendingEntryPoints.Remove(address);
        }
    }

    private void OnSpeculativeSeedCreated(CfgInstruction node) {
        if (node.DiscoverySource == ExploreSource.CatalogueEntry
            || _pendingEntryPoints.Remove(node.Address)) {
            RegisterEntryPoint(node);
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
        HardwareInterruptDeliveries.Clear();
        CurrentDepth = 0;
        CurrentExecutionContext = NewExecutionContext(SegmentedAddress.ZERO);
        ExecutingNode = null;
    }
}