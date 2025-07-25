namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Represents a node in the CFG graph.
/// </summary>
public interface ICfgNode {
    /// <summary>
    /// Unique identifier of the node
    /// </summary>
    int Id { get; }

    /// <summary>
    /// Nodes that were executed before this node
    /// </summary>
    HashSet<ICfgNode> Predecessors { get; }

    /// <summary>
    /// Nodes that were executed after this node
    /// </summary>
    HashSet<ICfgNode> Successors { get; }

    /// <summary>
    /// Address of the node in memory
    /// </summary>
    public SegmentedAddress Address { get; }

    /// <summary>
    /// Returns whether the node Live.
    /// Live means the node is in a state aligned with the machine state and can be executed.
    /// For an instruction, it means it is the same as the memory representation of the instruction.
    /// </summary>
    public bool IsLive { get; }

    /// <summary>
    /// True when the node execution can lead to going back to previous execution context if the next to execute is the correct address
    /// </summary>
    public bool CanCauseContextRestore { get; }

    /// <summary>
    /// Needs to be called each time a successor is added
    /// </summary>
    public void UpdateSuccessorCache();
    
    /// <summary>
    /// Execute this node
    /// </summary>
    /// <param name="helper">InstructionExecutionHelper instance providing access to the outside</param>
    public void Execute(InstructionExecutionHelper helper);

    /// <summary>
    /// Builds an Abstract Syntax Tree representing the grammar of the assembly instruction
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public InstructionNode ToInstructionAst(AstBuilder builder);

    /// <summary>
    /// Max successors this node can be expected to have.
    /// If null, it means the node can have an unlimited number of successors.
    /// </summary>
    public int? MaxSuccessorsCount { get; set; }
    
    /// <summary>
    /// Whether the node can have more successors in its current state 
    /// </summary>
    public bool CanHaveMoreSuccessors { get; set; }
    
    /// <summary>
    /// Direct access to successor for nodes with only one successor
    /// </summary>
    public ICfgNode? UniqueSuccessor { get; set; }
}