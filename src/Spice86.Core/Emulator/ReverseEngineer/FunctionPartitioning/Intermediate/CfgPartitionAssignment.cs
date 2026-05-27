namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

/// <summary>
/// Intermediate block-to-partition-draft assignment passed between pipeline stages.
/// </summary>
internal sealed record CfgPartitionAssignment {
    public required List<CfgPartitionDraft> Partitions { get; init; }
    public required Dictionary<CfgBlock, CfgPartitionDraft> PartitionByBlock { get; init; }

    /// <summary>
    /// Builds a block-to-partition dictionary from the given partition list.
    /// Synthetic partitions take precedence; ties are broken by partition id.
    /// </summary>
    internal static Dictionary<CfgBlock, CfgPartitionDraft> BuildBlockAssignment(List<CfgPartitionDraft> partitionDrafts) {
        Dictionary<CfgBlock, CfgPartitionDraft> partitionByBlock = new();
        foreach (CfgPartitionDraft partition in partitionDrafts.OrderBy(GetAssignmentPrecedence).ThenBy(partition => partition.Id)) {
            foreach (CfgBlock block in partition.Blocks) {
                partitionByBlock[block] = partition;
            }
        }
        return partitionByBlock;
    }

    private static int GetAssignmentPrecedence(CfgPartitionDraft partition) {
        if (partition.Kind == CfgCodePartitionKind.Synthetic) {
            return 1;
        }
        return 0;
    }
}
