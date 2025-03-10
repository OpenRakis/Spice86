namespace Spice86.Core.Emulator.CPU.CfgCpu.Linker;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.Function;

public class ExecutionContext {

    public ExecutionContext(int depth, FunctionHandler functionHandler) {
        Depth = depth;
        FunctionHandler = functionHandler;
    }

    public FunctionHandler FunctionHandler { get; }

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