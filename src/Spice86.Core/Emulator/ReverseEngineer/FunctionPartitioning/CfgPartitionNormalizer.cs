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
/// When one entry reaches another, the internal graph is first cut at the edges into a different entry
/// block so each entry dominates its own region, and the split is kept only when the graph of the
/// replacement partitions is acyclic.
/// </summary>
internal sealed class CfgPartitionNormalizer {
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
        // When one entry reaches another via ownership-preserving edges, the internal graph is cut
        // at the edges into a different entry block (self-loops on an entry stay) so each entry
        // dominates its own region; the cut edges become cross-partition flows through the edge
        // annotator. The split is kept only when the graph of the replacement partitions is
        // acyclic - a cycle between split methods could only run through the cyclic-jump
        // dispatcher or C# recursion - otherwise the partition keeps its switch.
        bool cutEntryEdges = HasEntryToEntryTransfer(entryBlocks, edgeIndex);

        // Build a dominator tree restricted to this partition's internal edges.
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock = BuildInternalSuccessors(partition.Blocks, edgeIndex, cutEntryEdges ? entryBlocks.ToHashSet() : []);
        DominatorTree<CfgBlock> dominatorTree = DominatorTreeBuilder.BuildFromEntries(partition.Blocks, entryBlocks, successorsByBlock, CfgBlockOrder.Instance);
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
        // multiple entries) are collected into residual partitions rather than dropped. The partition was
        // connected through the entry regions just split off, so what is left can fall apart: two shared
        // exits that each entry reaches but that never reach each other. Each connected region of the
        // residual becomes its own partition.
        HashSet<CfgBlock> residualBlocks = partition.Blocks.Where(block => !assignedBlocks.Contains(block)).ToHashSet();
        foreach (HashSet<CfgBlock> residualRegion in GetConnectedRegions(residualBlocks, edgeIndex)) {
            CfgBlock residualEntryBlock = GetFirstBlock(residualRegion);
            int id = useOriginalId ? partition.Id : idAllocator.AllocateId();
            useOriginalId = false;
            List<CfgCodePartitionEntry> residualEntries = GetResidualEntries(residualRegion, edgeIndex);
            CfgPartitionDraft residualPartition = CfgPartitionDraftFactory.CreateSynthetic(id, residualEntryBlock, residualRegion, residualEntries);
            replacements.Add(residualPartition);
        }

        if (replacements.Count <= 1) {
            return [];
        }
        if (cutEntryEdges && !IsAcyclic(partition, replacements, edgeIndex)) {
            return [];
        }
        return replacements;
    }

    /// <summary>Whether the graph of <paramref name="replacements"/> is acyclic: every internal
    /// ownership-preserving edge of the original partition whose source and target land in
    /// different replacements is an edge between them, and no strongly connected component of that
    /// graph may hold more than one replacement.</summary>
    private static bool IsAcyclic(CfgPartitionDraft partition, List<CfgPartitionDraft> replacements,
        CfgPartitionEdgeIndex edgeIndex) {
        Dictionary<CfgBlock, CfgPartitionDraft> replacementByBlock = new();
        foreach (CfgPartitionDraft replacement in replacements) {
            foreach (CfgBlock block in replacement.Blocks) {
                replacementByBlock[block] = replacement;
            }
        }
        Dictionary<CfgPartitionDraft, List<CfgPartitionDraft>> edgesByReplacement =
            new(ReferenceEqualityComparer.Instance);
        foreach (CfgBlock block in partition.Blocks) {
            CfgPartitionDraft source = replacementByBlock[block];
            foreach (CfgBlock target in edgeIndex.GetOutgoingEdges(block.Id)
                    .Where(edge => edge.Kind.IsOwnershipPreserving()
                        && partition.Blocks.Contains(edge.TargetBlock))
                    .Select(edge => edge.TargetBlock)
                    .Distinct()) {
                CfgPartitionDraft targetReplacement = replacementByBlock[target];
                if (ReferenceEquals(source, targetReplacement)) {
                    continue;
                }
                if (!edgesByReplacement.TryGetValue(source, out List<CfgPartitionDraft>? targets)) {
                    targets = new();
                    edgesByReplacement[source] = targets;
                }
                if (!targets.Contains(targetReplacement)) {
                    targets.Add(targetReplacement);
                }
            }
        }
        return StronglyConnectedComponents.Find(replacements,
                replacement => edgesByReplacement.GetValueOrDefault(replacement) ?? [])
            .Values.Distinct().Count() == replacements.Count;
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

    private static Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> BuildInternalSuccessors(HashSet<CfgBlock> blocks, CfgPartitionEdgeIndex edgeIndex, IReadOnlySet<CfgBlock> entryTargetsToCut) {
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock = new();
        foreach (CfgBlock block in blocks) {
            IReadOnlyList<CfgBlock> successors = CfgPartitionOrdering
                .BlocksByAddressAndId(
                    edgeIndex.GetOutgoingEdges(block.Id)
                        .Where(edge => edge.Kind.IsOwnershipPreserving() && blocks.Contains(edge.TargetBlock)
                            && (!entryTargetsToCut.Contains(edge.TargetBlock)
                                || edge.SourceBlock.Equals(edge.TargetBlock)))
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

    /// <summary>
    /// Splits <paramref name="blocks"/> into the regions connected by ownership-preserving edges between them,
    /// ignoring edge direction, in address order of each region's first block.
    /// </summary>
    private static List<HashSet<CfgBlock>> GetConnectedRegions(HashSet<CfgBlock> blocks, CfgPartitionEdgeIndex edgeIndex) {
        Dictionary<CfgBlock, HashSet<CfgBlock>> neighbors = BuildUndirectedNeighbors(blocks, edgeIndex);
        HashSet<CfgBlock> visited = new();
        return CfgPartitionOrdering.BlocksByAddressAndId(blocks)
            .Select(block => BreadthFirstSearch.Enumerate(block, current => neighbors.GetValueOrDefault(current) ?? [], visited).ToHashSet())
            .Where(region => region.Count > 0)
            .ToList();
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