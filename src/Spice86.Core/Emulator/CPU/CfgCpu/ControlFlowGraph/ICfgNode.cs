namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Represents a node in the CFG graph.
/// </summary>
public interface ICfgNode : IEquatable<ICfgNode> {
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
    /// Monotonically increasing counter incremented each time <see cref="CompiledExecution"/> is recompiled.
    /// Used by the background compiler to discard stale compiled delegates when a newer compilation has been requested.
    /// </summary>
    long CompilationGeneration { get; }

    /// <summary>
    /// Atomically increments <see cref="CompilationGeneration"/> and returns the new value.
    /// </summary>
    long IncrementCompilationGeneration();

    /// <summary>
    /// Compiled execution delegate generated from <see cref="ExecutionAst"/>.
    /// </summary>
    CfgNodeExecutionAction<InstructionExecutionHelper> CompiledExecution { get; set; }

    /// <summary>
    /// Determines the next successor node to execute based on current CPU / Memory state.
    /// </summary>
    /// <param name="helper">InstructionExecutionHelper instance providing access to CPU state and memory</param>
    /// <returns>The next node to execute, or null if no node exists in the graph for the state the machine is in</returns>
    ICfgNode? GetNextSuccessor(InstructionExecutionHelper helper);

    /// <summary>
    /// Pre-built Abstract Syntax Tree representing the grammar of the assembly instruction (for display).
    /// For a single instruction this is an <see cref="InstructionNode"/>; for a block it is a
    /// <see cref="BlockNode"/> wrapping all contained instructions' ASTs.
    /// </summary>
    IVisitableAstNode DisplayAst { get; }

    /// <summary>
    /// Pre-built Abstract Syntax Tree representing the execution logic of this node.
    /// This AST contains granular microcode-like operations that can be compiled to executable code.
    /// </summary>
    IVisitableAstNode ExecutionAst { get; }

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

    /// <summary>
    /// True when this node MUST be the last node of its <see cref="CfgBlock"/>.
    /// For a <see cref="CfgInstruction"/> this is computed from an explicit terminator flag set by the parser,
    /// the instruction's <c>Kind</c>, and the current value of <c>MaxSuccessorsCount</c>.
    /// For a <see cref="SelectorNode"/> this is always <c>true</c>.
    /// <see cref="Linker.NodeLinker"/> is the only consumer of this for boundary decisions; no other
    /// code SHALL inspect <c>Kind</c> or <c>MaxSuccessorsCount</c> for boundary purposes.
    /// </summary>
    bool IsBlockTerminator { get; }

    /// <summary>
    /// True when this node MUST be the first node of its <see cref="CfgBlock"/>, ending the
    /// preceding block before it. Stored explicit flag, set by parser only. Defaults to <c>false</c>.
    /// </summary>
    bool IsBlockStarter { get; }

    /// <summary>
    /// <see cref="CfgBlock"/> that contains this node, or <c>null</c> if the node is not (yet) a
    /// member of any block. <see cref="CfgBlock"/>'s own <c>ContainingBlock</c> returns <c>null</c>
    /// by definition: a block is itself a block, not a member of one.
    /// <see cref="Linker.NodeLinker"/> is the sole writer of this property.
    /// </summary>
    CfgBlock? ContainingBlock { get; set;  }
}