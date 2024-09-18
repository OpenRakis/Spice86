namespace Spice86.Core.Emulator.CPU.CfgCpu.Linker;

using Spice86.Core.Emulator.CPU.CfgCpu.CallFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

public class ExecutionContext {

    public ExecutionContext(int depth, CallFlowHandler callFlowHandler) {
        Depth = depth;
        CallFlowHandler = callFlowHandler;
    }

    public CallFlowHandler CallFlowHandler { get; }

    public int Depth { get; }

    /// <summary>
    /// Last node actually executed by the CPU
    /// </summary>
    public ICfgNode? LastExecuted { get; set; }
    
    /// <summary>
    /// Next node to execute according to the graph.
    /// </summary>
    public ICfgNode? NodeToExecuteNextAccordingToGraph { get; set; }
}