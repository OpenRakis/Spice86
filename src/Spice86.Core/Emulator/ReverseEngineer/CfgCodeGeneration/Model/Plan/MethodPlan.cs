namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

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
        PartitionBlockGraph blockGraph,
        DepthFirstOrdering<CfgBlock> blockTraversal) {
        Partition = partition;
        MethodName = methodName;
        Entries = entries;
        Blocks = blocks;
        Nodes = nodes;
        NodeEmissionPlans = nodeEmissionPlans;
        _nextNodeByNode = nextNodeByNode;
        BlockGraph = blockGraph;
        BlockTraversal = blockTraversal;
    }

    public CfgCodePartition Partition { get; }
    public string MethodName { get; }
    public IReadOnlyList<CfgCodePartitionEntry> Entries { get; }
    public CfgCodePartitionEntry PrimaryEntry => Entries[0];
    public IReadOnlyList<CfgBlock> Blocks { get; }
    public IReadOnlyList<ICfgNode> Nodes { get; }
    public IReadOnlyList<NodeEmissionPlan> NodeEmissionPlans { get; }
    public PartitionBlockGraph BlockGraph { get; }
    public DepthFirstOrdering<CfgBlock> BlockTraversal { get; }
    public bool NeedsEntryDispatch => Entries.Count > 1;

    public ICfgNode? GetNextEmittedNode(ICfgNode node) => _nextNodeByNode[node];
}
