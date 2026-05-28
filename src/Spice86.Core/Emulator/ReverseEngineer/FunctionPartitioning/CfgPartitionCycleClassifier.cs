namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

/// <summary>
/// Refines classified transfers that participate in generated activation cycles.
/// </summary>
internal sealed class CfgPartitionCycleClassifier {
    public List<CfgCodePartitionTransfer> Refine(
        IReadOnlyList<CfgCodePartition> partitions,
        IReadOnlyList<CfgCodePartitionTransfer> transfers) {
        Dictionary<int, List<int>> edgesByPartition = BuildActivationGraph(partitions, transfers);
        Dictionary<int, int> componentByPartition = FindStronglyConnectedComponents(edgesByPartition);
        return transfers
            .Select(transfer => ShouldUpgrade(transfer, componentByPartition)
                ? CopyWithKind(transfer, CfgCodePartitionTransferKind.CyclicCrossPartitionFlow)
                : transfer)
            .ToList();
    }

    private static Dictionary<int, List<int>> BuildActivationGraph(
        IReadOnlyList<CfgCodePartition> partitions,
        IReadOnlyList<CfgCodePartitionTransfer> transfers) {
        Dictionary<int, List<int>> edgesByPartition = new();
        foreach (CfgCodePartition partition in partitions) {
            EnsureVertex(edgesByPartition, partition.Id);
        }
        foreach (CfgCodePartitionTransfer transfer in transfers) {
            EnsureVertex(edgesByPartition, transfer.FromPartition.Id);
            EnsureVertex(edgesByPartition, transfer.ToPartition.Id);
            if (IsActivationEdge(transfer.Kind)) {
                edgesByPartition[transfer.FromPartition.Id].Add(transfer.ToPartition.Id);
            }
        }
        return edgesByPartition;
    }

    private static bool IsActivationEdge(CfgCodePartitionTransferKind kind) => kind
        is CfgCodePartitionTransferKind.CrossPartitionFlow
        or CfgCodePartitionTransferKind.CyclicCrossPartitionFlow
        or CfgCodePartitionTransferKind.CallOut
        or CfgCodePartitionTransferKind.DynamicReturn
        or CfgCodePartitionTransferKind.CpuFault;

    private static Dictionary<int, int> FindStronglyConnectedComponents(Dictionary<int, List<int>> edgesByPartition) {
        return StronglyConnectedComponents.Find(
            edgesByPartition.Keys.OrderBy(partition => partition),
            partition => edgesByPartition[partition]);
    }

    private static bool ShouldUpgrade(
        CfgCodePartitionTransfer transfer,
        Dictionary<int, int> componentByPartition) =>
        transfer.Kind == CfgCodePartitionTransferKind.CrossPartitionFlow
        && componentByPartition[transfer.FromPartition.Id] == componentByPartition[transfer.ToPartition.Id];

    private static CfgCodePartitionTransfer CopyWithKind(
        CfgCodePartitionTransfer transfer,
        CfgCodePartitionTransferKind kind) => new() {
        FromPartition = transfer.FromPartition,
        ToPartition = transfer.ToPartition,
        FromNode = transfer.FromNode,
        TargetNode = transfer.TargetNode,
        Kind = kind,
        CallContinuationNode = transfer.CallContinuationNode
    };

    private static void EnsureVertex(Dictionary<int, List<int>> edgesByPartition, int partitionId) {
        if (!edgesByPartition.ContainsKey(partitionId)) {
            edgesByPartition.Add(partitionId, []);
        }
    }
}