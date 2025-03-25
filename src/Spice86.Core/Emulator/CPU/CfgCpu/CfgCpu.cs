namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class CfgCpu : IInstructionExecutor, IFunctionHandlerProvider {
    private readonly ILoggerService _loggerService;
    private readonly InstructionExecutionHelper _instructionExecutionHelper;
    private readonly State _state;
    private readonly DualPic _dualPic;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly InstructionReplacerRegistry _replacerRegistry = new();

    public CfgCpu(IMemory memory, State state, IOPortDispatcher ioPortDispatcher, CallbackHandler callbackHandler,
        DualPic dualPic, EmulatorBreakpointsManager emulatorBreakpointsManager, FunctionCatalogue functionCatalogue, ILoggerService loggerService) {
        _loggerService = loggerService;
        _state = state;
        _dualPic = dualPic;
        
        CfgNodeFeeder = new(memory, state, emulatorBreakpointsManager, _replacerRegistry);
        _executionContextManager = new(memory, state, CfgNodeFeeder, _replacerRegistry, functionCatalogue, loggerService);
        _instructionExecutionHelper = new(state, memory, ioPortDispatcher, callbackHandler, emulatorBreakpointsManager.InterruptBreakPoints, _executionContextManager, loggerService);
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
    
    /// <inheritdoc />
    public void ExecuteNext() {
        ICfgNode toExecute = CfgNodeFeeder.GetLinkedCfgNodeToExecute(CurrentExecutionContext);

        // Execute the node
        try {
            _loggerService.LoggerPropertyBag.CsIp = toExecute.Address;
            toExecute.Execute(_instructionExecutionHelper);
        } catch (CpuException e) {
            if(toExecute is CfgInstruction cfgInstruction) {
                _instructionExecutionHelper.HandleCpuException(cfgInstruction, e);
            }
        }

        ICfgNode? nextToExecute = _instructionExecutionHelper.NextNode;
        
        _state.IncCycles();

        // Register what was executed and what is next node according to the graph in the execution context for next pass
        CurrentExecutionContext.LastExecuted = toExecute;
        CurrentExecutionContext.NodeToExecuteNextAccordingToGraph = nextToExecute;
        HandleExternalInterrupt(toExecute);
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
    
    private void HandleExternalInterrupt(ICfgNode toExecute) {
        // Before any external interrupt has a chance to execute, check if we landed in a place where context should be switched.
        if (toExecute.CanCauseContextRestore) {
            // We only attempt to restore contexts after IRET
            // Otherwise, we may hit via regular flow an instruction that is at the return address of an existing IRET and that is waiting to be restored, and restore it. 
            _executionContextManager.RestoreExecutionContextIfNeeded(_state.IpSegmentedAddress);
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
}