namespace Spice86.Core.Emulator.CPU.CfgCpu.Linker;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.Function;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// An execution context represents an area for the various systems that track execution flow.
/// Context changes when execution flow is disturbed by an external event (external interrupts) 
/// </summary>
public class ExecutionContext {

    public ExecutionContext(SegmentedAddress entryPoint, int depth, FunctionHandler functionHandler) {
        EntryPoint = entryPoint;
        Depth = depth;
        FunctionHandler = functionHandler;
    }

    /// <summary>
    /// Where the context started
    /// </summary>
    public SegmentedAddress EntryPoint { get; }
    
    /// <summary>
    /// Function handler tracking the functions for this context. Function call stack is context dependant.
    /// </summary>
    public FunctionHandler FunctionHandler { get; }

    /// <summary>
    /// Depth at which this context was created.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Last node actually executed by the CPU
    /// </summary>
    public ICfgNode? LastExecuted { get; set; }
    
    /// <summary>
    /// Next node to execute according to the CFG Graph.
    /// </summary>
    public ICfgNode? NodeToExecuteNextAccordingToGraph { get; set; }
}