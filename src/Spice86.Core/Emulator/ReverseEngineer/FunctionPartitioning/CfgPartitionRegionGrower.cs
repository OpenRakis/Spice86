namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

/// <summary>
/// Grows preliminary partition ownership regions from collected roots.
/// </summary>
internal sealed class CfgPartitionRegionGrower {
    public Dictionary<CfgBlock, HashSet<CfgPartitionRoot>> Grow(
        CfgPartitionEdgeIndex edgeIndex,
        List<CfgPartitionRoot> roots) {
        HashSet<int> rootBlockIds = roots.Select(root => root.EntryBlock.Id).ToHashSet();
        Dictionary<CfgBlock, HashSet<CfgPartitionRoot>> ownersByBlock = new();

        foreach (CfgPartitionRoot root in roots) {
            GrowRoot(root, edgeIndex, rootBlockIds, ownersByBlock);
        }

        return ownersByBlock;
    }

    private static void GrowRoot(
        CfgPartitionRoot root,
        CfgPartitionEdgeIndex edgeIndex,
        HashSet<int> rootBlockIds,
        Dictionary<CfgBlock, HashSet<CfgPartitionRoot>> ownersByBlock) {
        IEnumerable<CfgBlock> reachable = DepthFirstSearch.Enumerate(
            root.EntryBlock,
            block => GetOwnershipPreservingTargets(block, edgeIndex, rootBlockIds, root));
        foreach (CfgBlock block in reachable) {
            if (!ownersByBlock.TryGetValue(block, out HashSet<CfgPartitionRoot>? owners)) {
                owners = new HashSet<CfgPartitionRoot>();
                ownersByBlock.Add(block, owners);
            }
            owners.Add(root);
        }
    }

    private static IEnumerable<CfgBlock> GetOwnershipPreservingTargets(
        CfgBlock block,
        CfgPartitionEdgeIndex edgeIndex,
        HashSet<int> rootBlockIds,
        CfgPartitionRoot root) {
        IReadOnlyList<CfgPartitionEdgeRecord> outgoing = edgeIndex.GetOutgoingEdges(block.Id);
        if (outgoing.Count == 0) {
            return [];
        }
        // Stop at another root's entry block so regions do not bleed across partition boundaries.
        // Self-loops on this root's own entry block are allowed through.
        return CfgPartitionOrdering.ByBlockAddressAndId(outgoing, edge => edge.TargetBlock)
            .Where(edge => edge.Kind.IsOwnershipPreserving()
                && !(rootBlockIds.Contains(edge.TargetBlock.Id) && edge.TargetBlock.Id != root.EntryBlock.Id))
            .Select(edge => edge.TargetBlock);
    }
}
