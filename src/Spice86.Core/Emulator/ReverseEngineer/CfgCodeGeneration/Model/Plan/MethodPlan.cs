namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

internal sealed class MethodPlan {
    private readonly Dictionary<ICfgNode, ICfgNode?> _nextNodeByNode;

    internal MethodPlan(
        CfgCodePartition partition,
        string methodName,
        IReadOnlyList<CfgCodePartitionEntry> entries,
        IReadOnlyList<CfgBlock> blocks,
        IReadOnlyList<ICfgNode> nodes,
        IReadOnlyList<NodeEmissionPlan> nodeEmissionPlans,
        Dictionary<ICfgNode, ICfgNode?> nextNodeByNode,
        bool isCyclicFlowParticipant) {
        Partition = partition;
        MethodName = methodName;
        Entries = entries;
        Blocks = blocks;
        Nodes = nodes;
        NodeEmissionPlans = nodeEmissionPlans;
        _nextNodeByNode = nextNodeByNode;
        IsCyclicFlowParticipant = isCyclicFlowParticipant;
    }

    public CfgCodePartition Partition { get; }
    public string MethodName { get; }
    public IReadOnlyList<CfgCodePartitionEntry> Entries { get; }
    public CfgCodePartitionEntry PrimaryEntry => Entries[0];
    public IReadOnlyList<CfgBlock> Blocks { get; }
    public IReadOnlyList<ICfgNode> Nodes { get; }
    public IReadOnlyList<NodeEmissionPlan> NodeEmissionPlans { get; }
    public bool NeedsEntryDispatch => Entries.Count > 1;

    /// <summary>
    /// True when this partition is the source or target of a <c>CyclicCrossPartitionFlow</c> transfer. Such a
    /// transfer's back-edge re-enters the target method's <c>entrydispatcher</c> label, so a participant must
    /// emit that label even with a single entry.
    /// </summary>
    public bool IsCyclicFlowParticipant { get; }

    /// <summary>
    /// Whether the <c>entrydispatcher</c> label must be emitted: for multi-entry methods (the
    /// <c>loadOffset</c> switch) or cyclic-flow participants (the back-edge target).
    /// </summary>
    public bool NeedsEntryDispatchLabel => NeedsEntryDispatch || IsCyclicFlowParticipant;

    public ICfgNode? GetNextEmittedNode(ICfgNode node) => _nextNodeByNode[node];
}
