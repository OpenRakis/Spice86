namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Microsoft.Extensions.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

public class CfgCpu : IFunctionHandlerProvider, IClearable {
    private readonly ILoggerService _loggerService;
    private readonly InstructionExecutionHelper _instructionExecutionHelper;
    private readonly State _state;
    private readonly DualPic _dualPic;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly InstructionReplacerRegistry _replacerRegistry = new();
    private readonly CpuHeavyLogger? _cpuHeavyLogger;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;

    public CfgCpu(IMemory memory, State state, IOPortDispatcher ioPortDispatcher, CallbackHandler callbackHandler,
        DualPic dualPic, EmulatorBreakpointsManager emulatorBreakpointsManager,
        IPauseHandler pauseHandler,
        FunctionCatalogue functionCatalogue,
        bool useCodeOverride, bool failOnInvalidOpcode, bool allowIvtAddress0, bool enableSpeculativeExploration, ILoggerService loggerService, CfgNodeExecutionCompiler executionCompiler, SequentialIdAllocator idAllocator, CpuHeavyLogger? cpuHeavyLogger = null) {
        _loggerService = loggerService;
        _state = state;
        _dualPic = dualPic;
        _cpuHeavyLogger = cpuHeavyLogger;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        CfgNodeFeeder = new(memory, state, emulatorBreakpointsManager, _replacerRegistry, executionCompiler, idAllocator, enableSpeculativeExploration);
        _executionContextManager = new(memory, state, CfgNodeFeeder, _replacerRegistry, functionCatalogue, useCodeOverride, loggerService, cpuHeavyLogger);
        _instructionExecutionHelper = new(state, memory, ioPortDispatcher, callbackHandler, emulatorBreakpointsManager, _executionContextManager, failOnInvalidOpcode, allowIvtAddress0, loggerService);
    }

    /// <summary>
    /// Handles at high level parsing, linking and book keeping of nodes from the graph.
    /// </summary>
    public CfgNodeFeeder CfgNodeFeeder { get; }

    /// <summary>
    /// Handles the various contexts of the execution.
    /// A context contains a graph where links between instructions are due to regular execution flow internal to the CPU.
    /// Switching context is caused by external interrupts.
    /// </summary>
    public ExecutionContextManager ExecutionContextManager => _executionContextManager;

    public FunctionHandler FunctionHandlerInUse => ExecutionContextManager.CurrentExecutionContext.FunctionHandler;
    public bool IsInitialExecutionContext => ExecutionContextManager.CurrentExecutionContext.Depth == 0;
    private ExecutionContext CurrentExecutionContext => _executionContextManager.CurrentExecutionContext;

    public ICfgNode ToExecute() {
        return CfgNodeFeeder.GetLinkedCfgNodeToExecute(CurrentExecutionContext);
    }

    /// <summary>
    /// Returns the block to execute on the hot path if the node is the entry point of a discovered,
    /// live block, or null if the cold path should be taken.
    /// Entering a live block via something else than the entry point happens just after block discovery is completed.
    /// In this case, next is the terminator, appended to block, Block became complete but cold path needs to be done one last time.
    /// </summary>
    private CfgBlock? HotPathBlock(ICfgNode next) {
        if (next.ContainingBlock is { IsDiscoveryComplete: true, IsLive: true } block
            && next.Id == block.Entry.Id) {
            return block;
        }
        return null;
    }

    /// <summary>
    /// Resolves the next node via the cold-path entry edge, then dispatches hot or cold based
    /// on whether the node belongs to a discovered, live block. Hot path runs the block walker;
    /// cold path steps a single node. <see cref="HandleExternalInterrupt"/> fires exactly once
    /// at the boundary, on the last node actually executed.
    /// </summary>
    public void ExecuteNext() {
        ICfgNode next = ToExecute();
        ICfgNode lastExecuted;

        CfgBlock? hotBlock = HotPathBlock(next);
        if (hotBlock is not null) {
            lastExecuted = ExecuteBlock(hotBlock);
        } else {
            ExecuteOneNode(next);
            lastExecuted = next;
        }

        // After execution, advance ExecutingNode to the next node so that a loop-level
        // pause (EmulationLoop.WaitIfPaused) shows the node about to execute rather than
        // the one that just finished.
        ICfgNode? nextToExecute = CurrentExecutionContext.NodeToExecuteNextAccordingToGraph;
        if (nextToExecute is not null) {
            _executionContextManager.ExecutingNode = nextToExecute;
        }

        HandleExternalInterrupt(lastExecuted);
    }

    /// <summary>
    /// Executes a single CFG node: updates CS:IP logging, runs the compiled execution,
    /// handles any <see cref="CpuException"/> via the instruction execution helper, increments
    /// cycles, and records last-executed / next-to-execute state. Returns <c>false</c> when a
    /// CpuException was observed, signalling the caller to stop stepping.
    /// </summary>
    internal bool ExecuteOneNode(ICfgNode node) {
        _executionContextManager.ExecutingNode = node;
        if (_emulatorBreakpointsManager.HasActiveBreakpoints) {
            _emulatorBreakpointsManager.CheckExecutionBreakPointsAt(
                MemoryUtils.ToPhysicalAddress(node.Address.Segment, node.Address.Offset));
            if (_state.CS != node.Address.Segment || _state.IP != node.Address.Offset) {
                CurrentExecutionContext.NodeToExecuteNextAccordingToGraph = null;
                return false;
            }
            _pauseHandler.WaitIfPaused();
        }

        bool faulted = false;
        try {
            _loggerService.LoggerPropertyBag.CsIp = node.Address;
            _cpuHeavyLogger?.LogInstruction(node);
            node.CompiledExecution(_instructionExecutionHelper);
        } catch (CpuException e) {
            if (node is CfgInstruction cfgInstruction) {
                _instructionExecutionHelper.HandleCpuException(cfgInstruction, e);
            }
            faulted = true;
        }

        ICfgNode? nextToExecute = node.GetNextSuccessor(_instructionExecutionHelper);

        _state.IncCycles();

        CurrentExecutionContext.LastExecuted = node;
        CurrentExecutionContext.NodeToExecuteNextAccordingToGraph = nextToExecute;

        return !faulted;
    }

    /// <summary>
    /// Hot-path block walker: iterates a discovered, live <see cref="CfgBlock"/> from
    /// its entry through its terminator, calling <see cref="ExecuteOneNode"/>
    /// per instruction. Exits early if a node is non-live (memory mutated), a CpuException is
    /// observed, or the terminator is reached. Does not fire interrupts between steps.
    /// </summary>
    internal ICfgNode ExecuteBlock(CfgBlock block) {
        int count = block.Instructions.Count;
        ICfgNode lastExecuted = block.Entry;

        for (int i = 0; i < count; i++) {
            ICfgNode node = block.Instructions[i];

            if (!node.IsLive) {
                CurrentExecutionContext.NodeToExecuteNextAccordingToGraph = null;
                return lastExecuted;
            }

            bool ok = ExecuteOneNode(node);
            lastExecuted = node;

            if (!ok) {
                return lastExecuted;
            }
        }

        return lastExecuted;
    }

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    public void SignalEntry() {
        _executionContextManager.SignalEntry();
        // Parse the first instruction and register it as entry point
        CfgNodeFeeder.GetLinkedCfgNodeToExecute(CurrentExecutionContext);
        _executionContextManager.CurrentExecutionContext.FunctionHandler.Call(CallType.MACHINE, _state.IpSegmentedAddress, null, null);
        // expected return address from machine start is never defined.
        _executionContextManager.SignalNewExecutionContext(_state.IpSegmentedAddress, SegmentedAddress.ZERO);
    }

    public void SignalEnd() {
        _executionContextManager.CurrentExecutionContext.FunctionHandler.Ret(CallType.MACHINE, null);
    }

    /// <summary>
    /// Handles external interrupts that may need to be processed after the execution of a control flow graph (CFG) node.
    /// </summary>
    /// <param name="toExecute">The CFG node currently being executed, which may influence interrupt and context handling.</param>
    private void HandleExternalInterrupt(ICfgNode toExecute) {
        // Before any external interrupt has a chance to execute, check if we landed in a place where context should be switched.
        if (toExecute.CanCauseContextRestore) {
            // We only attempt to restore contexts after IRET
            // Otherwise, we may hit via regular flow an instruction that is at the return address of an existing IRET and that is waiting to be restored, and restore it.
            _executionContextManager.RestoreExecutionContextIfNeeded(_state.IpSegmentedAddress);
        }

        if (_state.InterruptShadowing) {
            // Interrupts are inhibited for this instruction only.
            // Is set either via POP SS, MOV SS/sreg or STI.
            _state.InterruptShadowing = false;
            return;
        }

        if (!_state.InterruptFlag) {
            return;
        }

        byte? externalInterruptVectorNumber = _dualPic.ComputeVectorNumber();
        if (externalInterruptVectorNumber == null) {
            return;
        }
        (SegmentedAddress target, SegmentedAddress expectedReturn) = _instructionExecutionHelper.DoInterrupt(externalInterruptVectorNumber.Value);

        _executionContextManager.SignalNewExecutionContext(target, expectedReturn);
        _executionContextManager.CurrentExecutionContext.FunctionHandler.Call(CallType.EXTERNAL_INTERRUPT, _state.IpSegmentedAddress, expectedReturn, null);
    }

    /// <inheritdoc />
    public void Clear() {
        CfgNodeFeeder.InstructionsFeeder.Clear();
        _executionContextManager.Clear();
    }
}
