namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Linq;

/// <summary>
/// Queryable index over partition edge records, pre-built once and reused across pipeline stages.
/// </summary>
internal sealed class CfgPartitionEdgeIndex {
    private readonly List<CfgPartitionEdgeRecord> _edgeRecords;
    private readonly Dictionary<int, List<CfgPartitionEdgeRecord>> _outgoingBySourceBlockId;
    private readonly Dictionary<int, List<CfgPartitionEdgeRecord>> _incomingByTargetBlockId;

    public CfgPartitionEdgeIndex(List<CfgPartitionEdgeRecord> edgeRecords) {
        _edgeRecords = edgeRecords;
        _outgoingBySourceBlockId = edgeRecords
            .GroupBy(edge => edge.SourceBlock.Id)
            .ToDictionary(group => group.Key, group => group.ToList());
        _incomingByTargetBlockId = edgeRecords
            .GroupBy(edge => edge.TargetBlock.Id)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    /// <summary>
    /// All edge records in the index.
    /// </summary>
    public IReadOnlyList<CfgPartitionEdgeRecord> EdgeRecords => _edgeRecords;

    /// <summary>
    /// Returns edges whose source is the given block.
    /// </summary>
    public IReadOnlyList<CfgPartitionEdgeRecord> GetOutgoingEdges(int sourceBlockId) {
        if (_outgoingBySourceBlockId.TryGetValue(sourceBlockId, out List<CfgPartitionEdgeRecord>? edges)) {
            return edges;
        }
        return [];
    }

    /// <summary>
    /// Returns edges whose target is the given block.
    /// </summary>
    public IReadOnlyList<CfgPartitionEdgeRecord> GetIncomingEdges(int targetBlockId) {
        if (_incomingByTargetBlockId.TryGetValue(targetBlockId, out List<CfgPartitionEdgeRecord>? edges)) {
            return edges;
        }
        return [];
    }

    /// <summary>
    /// Returns whether the given block has an ownership-preserving incoming edge from outside the specified set.
    /// </summary>
    public bool HasIncomingFromOutside(CfgBlock block, HashSet<CfgBlock> blocks) =>
        GetIncomingEdges(block.Id).Any(edge => edge.Kind.IsOwnershipPreserving() && !blocks.Contains(edge.SourceBlock));

    /// <summary>
    /// Returns whether the given block has any incoming edge from a different block.
    /// </summary>
    public bool HasIncomingFromDifferentBlock(CfgBlock block) =>
        GetIncomingEdges(block.Id).Any(edge => edge.SourceBlock.Id != block.Id);

    /// <summary>
    /// Returns whether the given block has any incoming edge from the same block (self-loop).
    /// </summary>
    public bool HasIncomingFromSameBlock(CfgBlock block) =>
        GetIncomingEdges(block.Id).Any(edge => edge.SourceBlock.Id == block.Id);
}
