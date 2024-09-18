namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.CallFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class CfgCpu : IInstructionExecutor {
    private readonly InstructionExecutionHelper _instructionExecutionHelper;
    private readonly State _state;
    private readonly DualPic _dualPic;
    private readonly CfgNodeFeeder _cfgNodeFeeder;
    private readonly ExecutionContextManager _executionContextManager;
    private readonly InstructionReplacerRegistry _replacerRegistry = new();

    public CfgCpu(IMemory memory, State state, IOPortDispatcher ioPortDispatcher, CallbackHandler callbackHandler,
        DualPic dualPic, EmulatorBreakpointsManager emulatorBreakpointsManager, ILoggerService loggerService) {
        _state = state;
        _dualPic = dualPic;
        
        _cfgNodeFeeder = new(memory, state, emulatorBreakpointsManager, _replacerRegistry);
        _executionContextManager = new(memory, state, emulatorBreakpointsManager, _cfgNodeFeeder, _replacerRegistry, loggerService);
        _instructionExecutionHelper = new(state, memory, ioPortDispatcher, callbackHandler, _executionContextManager, loggerService);
    }
    
    public ExecutionContextManager ExecutionContextManager => _executionContextManager;

    private ExecutionContext CurrentExecutionContext => _executionContextManager.CurrentExecutionContext;
    
    /// <inheritdoc />
    public void ExecuteNext() {
        ICfgNode toExecute = _cfgNodeFeeder.GetLinkedCfgNodeToExecute(CurrentExecutionContext);

        // Execute the node
        try {
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
        HandleExternalInterrupt();
    }

    /// <summary>
    /// Signal to the cfg cpu that we are at the entry point of the program
    /// </summary>
    public void SignalEntry() {
        _executionContextManager.SignalNewExecutionContext(_state.IpSegmentedAddress, null);
        _executionContextManager.CurrentExecutionContext.CallFlowHandler.Call(CallType.MACHINE, _state.IpSegmentedAddress, null, null);
    }
    
    private void HandleExternalInterrupt() {
        if (!_state.InterruptFlag) {
            return;
        }

        byte? externalInterruptVectorNumber = _dualPic.ComputeVectorNumber();
        if (externalInterruptVectorNumber == null) {
            return;
        }
        (SegmentedAddress target, SegmentedAddress expectedReturn) = _instructionExecutionHelper.DoInterrupt(externalInterruptVectorNumber.Value);

        _executionContextManager.SignalNewExecutionContext(target, expectedReturn);
        _executionContextManager.CurrentExecutionContext.CallFlowHandler.Call(CallType.INTERRUPT, _state.IpSegmentedAddress, expectedReturn, null);
    }
}