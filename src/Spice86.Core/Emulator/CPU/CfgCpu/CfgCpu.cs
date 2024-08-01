namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Shared.Emulator.Memory;

public class CfgCpu : IInstructionExecutor {
    private readonly InstructionExecutionHelper _instructionExecutionHelper;
    private readonly State _state;
    private readonly DualPic _dualPic;
    private readonly ExecutionContextManager _executionContextManager;

    private readonly CfgNodeFeeder _cfgNodeFeeder;

    public CfgCpu(InstructionExecutionHelper instructionExecutionHelper,
        ExecutionContextManager executionContextManager,
        CfgNodeFeeder cfgNodeFeeder,
        State state, DualPic dualPic) {
        _state = state;
        _dualPic = dualPic;
        _instructionExecutionHelper = instructionExecutionHelper;
        _cfgNodeFeeder = cfgNodeFeeder;
        _executionContextManager = executionContextManager;
    }

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
    }
}