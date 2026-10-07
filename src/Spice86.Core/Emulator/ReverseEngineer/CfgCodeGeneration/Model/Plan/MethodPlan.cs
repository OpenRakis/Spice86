namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

internal sealed class MethodPlan {
    private readonly IReadOnlyDictionary<ICfgNode, ICfgNode?> _nextNodeByNode;
    private readonly IReadOnlyDictionary<ICfgNode, string> _labelByNode;
    private readonly IReadOnlyDictionary<CfgInstruction, string> _localSuffixByInstruction;

    internal MethodPlan(
        CfgCodePartition partition,
        string methodName,
        IReadOnlyList<CfgCodePartitionEntry> entries,
        IReadOnlyList<CfgBlock> blocks,
        IReadOnlyList<ICfgNode> nodes,
        IReadOnlyList<NodeEmissionPlan> nodeEmissionPlans,
        IReadOnlyDictionary<ICfgNode, ICfgNode?> nextNodeByNode,
        PartitionBlockGraph blockGraph,
        DepthFirstOrdering<CfgBlock> blockTraversal,
        IReadOnlyDictionary<ICfgNode, string> labelByNode,
        IReadOnlyDictionary<CfgInstruction, string> localSuffixByInstruction) {
        Partition = partition;
        MethodName = methodName;
        Entries = entries;
        Blocks = blocks;
        Nodes = nodes;
        NodeEmissionPlans = nodeEmissionPlans;
        _nextNodeByNode = nextNodeByNode;
        BlockGraph = blockGraph;
        BlockTraversal = blockTraversal;
        _labelByNode = labelByNode;
        _localSuffixByInstruction = localSuffixByInstruction;
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

    /// <summary>The goto label of a block entry or method entry of this method.</summary>
    /// <exception cref="InvalidOperationException">The node is not a block entry or method entry of this method (a generator bug).</exception>
    public string GetLabel(ICfgNode node) {
        if (_labelByNode.TryGetValue(node, out string? label)) {
            return label;
        }
        throw new InvalidOperationException($"Node {node.Address} (id {node.Id}) is not a block entry or method entry of {MethodName}.");
    }

    /// <summary>The local variable suffix for an instruction of this method.</summary>
    /// <exception cref="InvalidOperationException">The instruction is not part of this method (a generator bug).</exception>
    public string GetLocalSuffix(CfgInstruction instruction) {
        if (_localSuffixByInstruction.TryGetValue(instruction, out string? suffix)) {
            return suffix;
        }
        throw new InvalidOperationException($"Instruction {instruction.Address} (id {instruction.Id}) is not part of {MethodName}.");
    }
}
