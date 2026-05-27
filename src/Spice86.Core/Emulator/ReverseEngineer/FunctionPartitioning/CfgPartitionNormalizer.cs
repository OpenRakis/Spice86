namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

using SequentialIdAllocator = Spice86.Shared.Utils.SequentialIdAllocator;

/// <summary>
/// Refines assembled partition assignment into simpler single-entry synthetic regions when dominance permits it.
/// </summary>
internal sealed class CfgPartitionNormalizer {
    private readonly CfgBlockDominatorTreeBuilder _dominatorTreeBuilder = new();

    public CfgPartitionAssignment Normalize(CfgPartitionAssignment assignment, CfgPartitionEdgeIndex edgeIndex, SequentialIdAllocator idAllocator) {
        List<CfgPartitionDraft> updatedPartitions = new();
        foreach (CfgPartitionDraft partition in assignment.Partitions.OrderBy(partition => partition.Id)) {
            if (!CanNormalize(partition)) {
                updatedPartitions.Add(partition);
                continue;
            }
            List<CfgPartitionDraft> normalizedPartitions = NormalizePartition(partition, edgeIndex, idAllocator);
            if (normalizedPartitions.Count == 0) {
                updatedPartitions.Add(partition);
                continue;
            }
            updatedPartitions.AddRange(normalizedPartitions);
        }

        return new CfgPartitionAssignment {
            Partitions = updatedPartitions.OrderBy(partition => partition.Id).ToList(),
            PartitionByBlock = CfgPartitionAssignment.BuildBlockAssignment(updatedPartitions)
        };
    }

    private static bool CanNormalize(CfgPartitionDraft partition) =>
        partition.Kind == CfgCodePartitionKind.Synthetic
        && GetEntryBlocks(partition).Count > 1;

    private List<CfgPartitionDraft> NormalizePartition(CfgPartitionDraft partition, CfgPartitionEdgeIndex edgeIndex, SequentialIdAllocator idAllocator) {
        List<CfgBlock> entryBlocks = GetEntryBlocks(partition);
        // If one entry block can reach another via ownership-preserving edges, their relative
        // ordering is load-bearing and splitting would break the activation flow, bail out.
        if (HasEntryToEntryTransfer(entryBlocks, edgeIndex)) {
            return [];
        }

        // Build a dominator tree restricted to this partition's internal edges.
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock = BuildInternalSuccessors(partition.Blocks, edgeIndex);
        CfgBlockDominatorTree dominatorTree = _dominatorTreeBuilder.BuildFromEntries(partition.Blocks, entryBlocks, successorsByBlock);
        List<CfgPartitionDraft> replacements = new();
        HashSet<CfgBlock> assignedBlocks = new();
        // Reuse the original partition ID for the first replacement to keep external references stable.
        bool useOriginalId = true;
        foreach (CfgBlock entryBlock in entryBlocks) {
            // Each entry block that strictly dominates a non-empty sub-set becomes its own partition.
            HashSet<CfgBlock> dominatedBlocks = partition.Blocks
                .Where(block => dominatorTree.Dominates(entryBlock, block))
                .ToHashSet();
            if (dominatedBlocks.Count == 0) {
                continue;
            }
            int id = useOriginalId ? partition.Id : idAllocator.AllocateId();
            useOriginalId = false;
            CfgPartitionDraft replacement = CfgPartitionDraftFactory.CreateSynthetic(id, entryBlock, dominatedBlocks, [CfgPartitionDraftFactory.CreateSharedEntry(entryBlock)]);
            replacements.Add(replacement);
            assignedBlocks.UnionWith(dominatedBlocks);
        }

        // Blocks not dominated by any single entry block (e.g., shared exit paths reachable from
        // multiple entries) are collected into a residual partition rather than dropped.
        HashSet<CfgBlock> residualBlocks = partition.Blocks.Where(block => !assignedBlocks.Contains(block)).ToHashSet();
        if (residualBlocks.Count > 0) {
            EnsureResidualBlocksConnected(residualBlocks, edgeIndex);
            CfgBlock residualEntryBlock = GetFirstBlock(residualBlocks);
            int id = useOriginalId ? partition.Id : idAllocator.AllocateId();
            List<CfgCodePartitionEntry> residualEntries = GetResidualEntries(residualBlocks, edgeIndex);
            CfgPartitionDraft residualPartition = CfgPartitionDraftFactory.CreateSynthetic(id, residualEntryBlock, residualBlocks, residualEntries);
            replacements.Add(residualPartition);
        }

        if (replacements.Count <= 1) {
            return [];
        }
        return replacements;
    }

    private static List<CfgBlock> GetEntryBlocks(CfgPartitionDraft partition) => CfgPartitionOrdering
        .BlocksByAddressAndId(partition.Entries.Select(entry => entry.Block).Distinct())
        .ToList();

    private static bool HasEntryToEntryTransfer(List<CfgBlock> entryBlocks, CfgPartitionEdgeIndex edgeIndex) {
        HashSet<CfgBlock> entryBlockSet = entryBlocks.ToHashSet();
        return entryBlocks.Any(entryBlock =>
            edgeIndex.GetOutgoingEdges(entryBlock.Id).Any(edge =>
                edge.Kind.IsOwnershipPreserving()
                && !edge.SourceBlock.Equals(edge.TargetBlock)
                && entryBlockSet.Contains(edge.TargetBlock)));
    }

    private static Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> BuildInternalSuccessors(HashSet<CfgBlock> blocks, CfgPartitionEdgeIndex edgeIndex) {
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock = new();
        foreach (CfgBlock block in blocks) {
            IReadOnlyList<CfgBlock> successors = CfgPartitionOrdering
                .BlocksByAddressAndId(
                    edgeIndex.GetOutgoingEdges(block.Id)
                        .Where(edge => edge.Kind.IsOwnershipPreserving() && blocks.Contains(edge.TargetBlock))
                        .Select(edge => edge.TargetBlock)
                        .Distinct())
                .ToArray();
            if (successors.Count > 0) {
                successorsByBlock[block] = successors;
            }
        }
        return successorsByBlock;
    }

    private static List<CfgCodePartitionEntry> GetResidualEntries(HashSet<CfgBlock> residualBlocks, CfgPartitionEdgeIndex edgeIndex) {
        IEnumerable<CfgBlock> candidateEntryBlocks = residualBlocks
            .Where(block => edgeIndex.HasIncomingFromOutside(block, residualBlocks));
        List<CfgBlock> entryBlocks = CfgPartitionOrdering.BlocksByAddressAndId(candidateEntryBlocks).ToList();
        if (entryBlocks.Count == 0) {
            entryBlocks.Add(GetFirstBlock(residualBlocks));
        }
        return entryBlocks.Select(CfgPartitionDraftFactory.CreateSharedEntry).ToList();
    }

    private static CfgBlock GetFirstBlock(HashSet<CfgBlock> blocks) => CfgPartitionOrdering.BlocksByAddressAndId(blocks).First();

    private static void EnsureResidualBlocksConnected(HashSet<CfgBlock> residualBlocks, CfgPartitionEdgeIndex edgeIndex) {
        if (residualBlocks.Count <= 1) {
            return;
        }
        Dictionary<CfgBlock, HashSet<CfgBlock>> neighbors = BuildUndirectedNeighbors(residualBlocks, edgeIndex);
        CfgBlock start = GetFirstBlock(residualBlocks);
        int reachableCount = BreadthFirstSearch.Enumerate(start, block => neighbors.GetValueOrDefault(block) ?? []).Count();
        if (reachableCount != residualBlocks.Count) {
            throw new InvalidOperationException(
                $"Residual partition contains {residualBlocks.Count} blocks but only {reachableCount} are reachable from the first block. " +
                "The partition assembler placed blocks from unrelated execution paths in the same partition.");
        }
    }

    private static Dictionary<CfgBlock, HashSet<CfgBlock>> BuildUndirectedNeighbors(HashSet<CfgBlock> blocks, CfgPartitionEdgeIndex edgeIndex) {
        Dictionary<CfgBlock, HashSet<CfgBlock>> neighbors = new();
        foreach (CfgBlock block in blocks) {
            foreach (CfgPartitionEdgeRecord edge in edgeIndex.GetOutgoingEdges(block.Id)) {
                if (!edge.Kind.IsOwnershipPreserving() || !blocks.Contains(edge.TargetBlock)) {
                    continue;
                }
                if (!neighbors.TryGetValue(block, out HashSet<CfgBlock>? blockNeighbors)) {
                    blockNeighbors = new();
                    neighbors[block] = blockNeighbors;
                }
                blockNeighbors.Add(edge.TargetBlock);
                if (!neighbors.TryGetValue(edge.TargetBlock, out HashSet<CfgBlock>? targetNeighbors)) {
                    targetNeighbors = new();
                    neighbors[edge.TargetBlock] = targetNeighbors;
                }
                targetNeighbors.Add(block);
            }
        }
        return neighbors;
    }

}