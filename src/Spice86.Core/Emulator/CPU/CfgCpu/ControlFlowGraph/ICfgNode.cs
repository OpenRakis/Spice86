namespace Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Represents a node in the CFG graph.
/// </summary>
public interface ICfgNode {
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
    /// True when the node represents an assembly instruction that exists or has existed in memory
    /// </summary>
    public bool IsAssembly { get; }

    /// <summary>
    /// Needs to be called each time a successor is added
    /// </summary>
    public void UpdateSuccessorCache();

    /// <summary>
    /// Visit this node
    /// </summary>
    /// <param name="visitor">Visitor for this node</param>
    public void Visit(ICfgNodeVisitor visitor);
}