namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
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
    SegmentedAddress Address { get; }

    /// <summary>
    /// Returns whether the node Live.
    /// Live means the node is in a state aligned with the machine state and can be executed.
    /// For an instruction, it means it is the same as the memory representation of the instruction.
    /// </summary>
    bool IsLive { get; }

    /// <summary>
    /// True when the node execution can lead to going back to previous execution context if the next to execute is the correct address
    /// </summary>
    bool CanCauseContextRestore { get; }

    /// <summary>
    /// Needs to be called each time a successor is added
    /// </summary>
    void UpdateSuccessorCache();
    
    /// <summary>
    /// Execute this node
    /// </summary>
    /// <param name="helper">InstructionExecutionHelper instance providing access to the outside</param>
    void Execute(InstructionExecutionHelper helper);

    /// <summary>
    /// Builds an Abstract Syntax Tree representing the grammar of the assembly instruction
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    InstructionNode ToInstructionAst(AstBuilder builder);

    /// <summary>
    /// Generates an Abstract Syntax Tree representing the execution semantics of the instruction.
    /// The returned AST contains granular microcode-like operations that describe how the instruction executes.
    /// </summary>
    /// <param name="builder">The builder to use for constructing AST nodes</param>
    /// <returns>An AST node representing the instruction's execution logic</returns>
    IVisitableAstNode GetExecutionAst(AstBuilder builder);

    /// <summary>
    /// Max successors this node can be expected to have.
    /// If null, it means the node can have an unlimited number of successors.
    /// </summary>
    int? MaxSuccessorsCount { get; set; }
    
    /// <summary>
    /// Whether the node can have more successors in its current state 
    /// </summary>
    bool CanHaveMoreSuccessors { get; set; }
    
    /// <summary>
    /// Direct access to successor for nodes with only one successor
    /// </summary>
    ICfgNode? UniqueSuccessor { get; set; }
}