namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

/// <summary>
/// Classifies inter-partition transfer records.
/// </summary>
internal sealed class CfgPartitionEdgeAnnotator {
    public List<CfgCodePartitionTransfer> CollectTransfers(
        CfgPartitionEdgeIndex edgeIndex,
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock) {
        EdgeAnnotationIndex index = new(edgeIndex.EdgeRecords, partitionByBlock);
        List<CfgCodePartitionTransfer> transfers = new();
        foreach (CfgPartitionEdgeRecord edge in edgeIndex.EdgeRecords) {
            CfgCodePartition sourcePartition = partitionByBlock[edge.SourceBlock];
            CfgCodePartition targetPartition = partitionByBlock[edge.TargetBlock];
            if (sourcePartition.Id == targetPartition.Id) {
                continue;
            }
            CfgCodePartitionTransferKind transferKind = ClassifyPartitionExit(edge, index, sourcePartition);
            CfgCodePartitionTransfer transfer = new() {
                FromPartition = sourcePartition,
                ToPartition = targetPartition,
                FromNode = edge.SourceNode,
                TargetNode = edge.TargetNode,
                Kind = transferKind,
                CallContinuationNode = transferKind == CfgCodePartitionTransferKind.CallOut
                    ? index.FindCallContinuation(edge)?.TargetNode
                    : null
            };
            transfers.Add(transfer);
        }

        return transfers
            .OrderBy(transfer => transfer.FromPartition.Id)
            .ThenBy(transfer => transfer.ToPartition.Id)
            .ThenBy(transfer => transfer.FromBlock.Id)
            .ThenBy(transfer => transfer.ToBlock.Id)
            .ThenBy(transfer => transfer.Kind)
            .ToList();
    }

    private static CfgCodePartitionTransferKind ClassifyPartitionExit(
        CfgPartitionEdgeRecord edge,
        EdgeAnnotationIndex index,
        CfgCodePartition returningPartition) {
        if (edge.Kind == ClassifiedEdgeKind.Call) {
            return CfgCodePartitionTransferKind.CallOut;
        }
        if (edge.Kind == ClassifiedEdgeKind.CpuFault) {
            return CfgCodePartitionTransferKind.CpuFault;
        }
        if (edge.Kind == ClassifiedEdgeKind.RetTarget) {
            // Only CallContinuation qualifies: MisalignedCallContinuation means the ret
            // did not land at the call's next instruction, so the pair cannot be represented
            // as a balanced C# call/return.
            return index.HasMatchingCallContinuation(edge, returningPartition, ClassifiedEdgeKind.CallContinuation)
                ? CfgCodePartitionTransferKind.AlignedReturn
                : CfgCodePartitionTransferKind.DynamicReturn;
        }
        return CfgCodePartitionTransferKind.CrossPartitionFlow;
    }

}
