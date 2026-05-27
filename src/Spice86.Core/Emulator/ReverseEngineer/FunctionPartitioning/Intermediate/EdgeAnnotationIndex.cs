namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Utils;

using System.Linq;

internal sealed class EdgeAnnotationIndex {
    private readonly Dictionary<CfgBlock, CfgCodePartition> _partitionByBlock;
    private readonly Dictionary<ContinuationTargetKey, List<CfgPartitionEdgeRecord>> _continuationEdgesByTarget = new();
    private readonly Dictionary<int, List<CfgPartitionEdgeRecord>> _callEdgesBySourceNodeId = new();
    private readonly Dictionary<int, List<CfgPartitionEdgeRecord>> _continuationEdgesBySourceNodeId = new();
    private readonly Dictionary<int, HashSet<int>> _sameActivationTargetIdsBySourcePartitionId = new();
    private readonly Dictionary<(int StartPartitionId, int TargetPartitionId), bool> _sameActivationReachability = new();

    public EdgeAnnotationIndex(
        IReadOnlyList<CfgPartitionEdgeRecord> edgeRecords,
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock) {
        _partitionByBlock = partitionByBlock;
        foreach (CfgPartitionEdgeRecord edge in edgeRecords) {
            IndexByKind(edge);
            IndexSameActivationEdge(edge);
        }
    }

    public bool HasMatchingCallContinuation(
        CfgPartitionEdgeRecord returnEdge,
        CfgCodePartition returningPartition,
        ClassifiedEdgeKind continuationKind) {
        ContinuationTargetKey continuationTargetKey = new(continuationKind, returnEdge.TargetBlock.Id, returnEdge.TargetNode.Address);
        // Primary path: find a continuation edge at the ret target whose originating CALL node
        // could have transferred control into the returning partition via same-activation edges.
        if (GetEdges(_continuationEdgesByTarget, continuationTargetKey).Any(continuationEdge =>
            GetEdges(_callEdgesBySourceNodeId, continuationEdge.SourceNode.Id).Any(callEdge =>
                CanReachFromCallTarget(callEdge, returningPartition)))) {
            return true;
        }

        // Fallback: no explicit call node recorded, but the continuation source partition can still
        // reach the returning partition through intra-activation ownership-preserving edges.
        return GetEdges(_continuationEdgesByTarget, continuationTargetKey).Any(continuationEdge =>
            _partitionByBlock.TryGetValue(continuationEdge.SourceBlock, out CfgCodePartition? callSourcePartition)
            && CanReachThroughSameActivation(callSourcePartition, returningPartition));
    }

    public CfgPartitionEdgeRecord? FindCallContinuation(CfgPartitionEdgeRecord callEdge) =>
        GetEdges(_continuationEdgesBySourceNodeId, callEdge.SourceNode.Id)
            .OrderBy(edge => GetContinuationKindOrder(edge.Kind))
            .ThenBy(edge => edge.TargetBlock.Entry.Address)
            .ThenBy(edge => edge.TargetBlock.Id)
            .FirstOrDefault();

    private static int GetContinuationKindOrder(ClassifiedEdgeKind kind) {
        if (kind == ClassifiedEdgeKind.CallContinuation) {
            return 0;
        }
        return 1;
    }

    private void IndexByKind(CfgPartitionEdgeRecord edge) {
        if (edge.Kind == ClassifiedEdgeKind.Call) {
            AddEdge(_callEdgesBySourceNodeId, edge.SourceNode.Id, edge);
            return;
        }

        if (edge.Kind != ClassifiedEdgeKind.CallContinuation && edge.Kind != ClassifiedEdgeKind.MisalignedCallContinuation) {
            return;
        }

        ContinuationTargetKey continuationTargetKey = new(edge.Kind, edge.TargetBlock.Id, edge.TargetNode.Address);
        AddEdge(_continuationEdgesByTarget, continuationTargetKey, edge);
        AddEdge(_continuationEdgesBySourceNodeId, edge.SourceNode.Id, edge);
    }

    private void IndexSameActivationEdge(CfgPartitionEdgeRecord edge) {
        // Collect ownership-preserving cross-partition edges to build a reachability graph used
        // by CanReachThroughSameActivation: if partitionA can reach partitionB through such edges,
        // they belong to the same activation frame and a ret to B is valid for a call from A.
        if (!edge.Kind.IsOwnershipPreserving()
            || !_partitionByBlock.TryGetValue(edge.SourceBlock, out CfgCodePartition? sourcePartition)
            || !_partitionByBlock.TryGetValue(edge.TargetBlock, out CfgCodePartition? targetPartition)) {
            return;
        }
        AddTargetPartitionId(sourcePartition.Id, targetPartition.Id);
    }

    private bool CanReachFromCallTarget(CfgPartitionEdgeRecord callEdge, CfgCodePartition returningPartition) {
        if (!_partitionByBlock.TryGetValue(callEdge.TargetBlock, out CfgCodePartition? calleePartition)) {
            return false;
        }
        return CanReachThroughSameActivation(calleePartition, returningPartition);
    }

    private bool CanReachThroughSameActivation(CfgCodePartition startPartition, CfgCodePartition targetPartition) {
        (int StartPartitionId, int TargetPartitionId) key = (startPartition.Id, targetPartition.Id);
        if (_sameActivationReachability.TryGetValue(key, out bool cachedResult)) {
            return cachedResult;
        }

        bool result = ComputeCanReachThroughSameActivation(startPartition.Id, targetPartition.Id);
        _sameActivationReachability[key] = result;
        return result;
    }

    private bool ComputeCanReachThroughSameActivation(int startPartitionId, int targetPartitionId) {
        return DepthFirstSearch.CanReach(
            startPartitionId,
            targetPartitionId,
            id => _sameActivationTargetIdsBySourcePartitionId.TryGetValue(id, out HashSet<int>? targets)
                ? targets
                : Enumerable.Empty<int>());
    }

    private void AddTargetPartitionId(int sourcePartitionId, int targetPartitionId) {
        if (!_sameActivationTargetIdsBySourcePartitionId.TryGetValue(sourcePartitionId, out HashSet<int>? targetPartitionIds)) {
            targetPartitionIds = new();
            _sameActivationTargetIdsBySourcePartitionId[sourcePartitionId] = targetPartitionIds;
        }
        targetPartitionIds.Add(targetPartitionId);
    }

    private static void AddEdge<TKey>(Dictionary<TKey, List<CfgPartitionEdgeRecord>> edgesByKey, TKey key, CfgPartitionEdgeRecord edge)
        where TKey : notnull =>
        DictionaryUtils.GetOrAddList(edgesByKey, key).Add(edge);

    private static IReadOnlyList<TValue> GetEdges<TKey, TValue>(Dictionary<TKey, List<TValue>> valuesByKey, TKey key)
        where TKey : notnull {
        if (valuesByKey.TryGetValue(key, out List<TValue>? values)) {
            return values;
        }
        return [];
    }
}