namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

public class CfgCpu : IDebuggableComponent {
    private readonly InstructionExecutionHelper _instructionExecutionHelper;
    private readonly State _state;
    private readonly DualPic _dualPic;

    private readonly CfgNodeFeeder _cfgNodeFeeder;

    public CfgCpu(IMemory memory, State state, IOPortDispatcher? ioPortDispatcher, CallbackHandler callbackHandler,
        DualPic dualPic, MachineBreakpoints machineBreakpoints) {
        _instructionExecutionHelper = new(state, memory, ioPortDispatcher, callbackHandler);
        _state = state;
        _dualPic = dualPic;
        _cfgNodeFeeder = new(memory, state, machineBreakpoints);
    }

    private ExecutionContext CurrentExecutionContext { get; } = new();

    public void Accept<T>(T emulatorDebugger) where T : IInternalDebugger {
        emulatorDebugger.Visit(this);
        _state.Accept(emulatorDebugger);
        CurrentExecutionContext.Accept(emulatorDebugger);
    }

    public void ExecuteNext() {
        ICfgNode toExecute = _cfgNodeFeeder.GetLinkedCfgNodeToExecute(CurrentExecutionContext);

        // Execute the node
        toExecute.Execute(_instructionExecutionHelper);
        ICfgNode? nextToExecute = _instructionExecutionHelper.NextNode;
        _state.IncCycles();

        // Register what was executed and what is next node according to the graph in the execution context for next pass
        CurrentExecutionContext.LastExecuted = toExecute;
        CurrentExecutionContext.NodeToExecuteNextAccordingToGraph = nextToExecute;
    }
}