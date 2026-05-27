namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Utils;

using System.Linq;

using SequentialIdAllocator = Spice86.Shared.Utils.SequentialIdAllocator;

/// <summary>
/// Converts preliminary ownership evidence into observed and synthetic partition drafts.
/// </summary>
internal sealed class CfgPartitionAssembler {
    public CfgPartitionAssignment Assemble(
        List<CfgBlock> blocks,
        CfgPartitionEdgeIndex edgeIndex,
        List<CfgPartitionRoot> roots,
        Dictionary<CfgBlock, HashSet<CfgPartitionRoot>> ownersByBlock,
        SequentialIdAllocator idAllocator) {
        List<CfgPartitionDraft> partitionDrafts = new();
        Dictionary<CfgPartitionRoot, CfgPartitionDraft> observedByRoot = new();
        AddObservedPartitions(roots, partitionDrafts, observedByRoot, idAllocator);
        HashSet<CfgBlock> sharedBlocks = ownersByBlock
            .Where(entry => entry.Value.Count > 1)
            .Select(entry => entry.Key)
            .ToHashSet();

        AddSyntheticPartitions(sharedBlocks, edgeIndex, roots, partitionDrafts, idAllocator);
        AddOwnedBlocks(blocks, ownersByBlock, sharedBlocks, observedByRoot);

        return new CfgPartitionAssignment {
            Partitions = partitionDrafts,
            PartitionByBlock = CfgPartitionAssignment.BuildBlockAssignment(partitionDrafts)
        };
    }

    private static void AddObservedPartitions(
        List<CfgPartitionRoot> roots,
        List<CfgPartitionDraft> partitionDrafts,
        Dictionary<CfgPartitionRoot, CfgPartitionDraft> observedByRoot,
        SequentialIdAllocator idAllocator) {
        foreach (CfgPartitionRoot root in roots) {
            CfgPartitionDraft partition = new(idAllocator.AllocateId(), root.Kind, root.EntryBlock, root.Name);
            partition.Entries.AddRange(root.Entries);
            partitionDrafts.Add(partition);
            observedByRoot.Add(root, partition);
        }
    }

    private static void AddSyntheticPartitions(
        HashSet<CfgBlock> sharedBlocks,
        CfgPartitionEdgeIndex edgeIndex,
        List<CfgPartitionRoot> roots,
        List<CfgPartitionDraft> partitionDrafts,
        SequentialIdAllocator idAllocator) {
        foreach (HashSet<CfgBlock> component in BuildSharedComponents(sharedBlocks, edgeIndex)) {
            List<CfgBlock> entryBlocks = GetSyntheticEntryBlocks(component, edgeIndex, roots);
            CfgBlock entryBlock = entryBlocks[0];
            List<CfgCodePartitionEntry> entries = entryBlocks.Select(CfgPartitionDraftFactory.CreateSharedEntry).ToList();
            CfgPartitionDraft synthetic = CfgPartitionDraftFactory.CreateSynthetic(idAllocator.AllocateId(), entryBlock, component, entries);
            partitionDrafts.Add(synthetic);
        }
    }

    private static void AddOwnedBlocks(
        List<CfgBlock> blocks,
        Dictionary<CfgBlock, HashSet<CfgPartitionRoot>> ownersByBlock,
        HashSet<CfgBlock> sharedBlocks,
        Dictionary<CfgPartitionRoot, CfgPartitionDraft> observedByRoot) {
        foreach (CfgBlock block in blocks.Where(block => !sharedBlocks.Contains(block))) {
            if (!ownersByBlock.TryGetValue(block, out HashSet<CfgPartitionRoot>? owners) || owners.Count == 0) {
                throw new InvalidOperationException($"Cannot assemble ownerless CFG block {block.Id} at {block.Entry.Address}.");
            }
            CfgPartitionRoot owner = owners.First();
            // Blocks with only one owner (non-shared)
            observedByRoot[owner].Blocks.Add(block);
        }
    }

    // Groups shared blocks into connected components via ownership-preserving edges.
    // Two shared blocks are placed in the same synthetic partition when they are mutually
    // reachable through ownership-preserving edges (fallthrough, jump, aligned call-continuation).
    // Splitting such a component would create an invalid ownership boundary in the middle of a
    // single activation, so each component becomes exactly one synthetic partition.
    private static List<HashSet<CfgBlock>> BuildSharedComponents(HashSet<CfgBlock> sharedBlocks, CfgPartitionEdgeIndex edgeIndex) {
        HashSet<CfgBlock> visited = new();
        Dictionary<int, List<CfgBlock>> neighbors = BuildSharedNeighbors(sharedBlocks, edgeIndex);
        return CfgPartitionOrdering.BlocksByAddressAndId(sharedBlocks)
            .Select(block => DepthFirstSearch.Enumerate(
                block,
                current => neighbors.TryGetValue(current.Id, out List<CfgBlock>? n) ? n : [],
                visited).ToHashSet())
            .Where(component => component.Count > 0)
            .ToList();
    }

    private static Dictionary<int, List<CfgBlock>> BuildSharedNeighbors(HashSet<CfgBlock> sharedBlocks, CfgPartitionEdgeIndex edgeIndex) {
        Dictionary<int, List<CfgBlock>> neighbors = new();
        foreach (CfgPartitionEdgeRecord edge in edgeIndex.EdgeRecords
            .Where(edge => edge.Kind.IsOwnershipPreserving())
            .Where(edge => sharedBlocks.Contains(edge.SourceBlock) && sharedBlocks.Contains(edge.TargetBlock))) {
            AddNeighbor(neighbors, edge.SourceBlock, edge.TargetBlock);
            AddNeighbor(neighbors, edge.TargetBlock, edge.SourceBlock);
        }
        return neighbors;
    }

    private static void AddNeighbor(Dictionary<int, List<CfgBlock>> neighbors, CfgBlock from, CfgBlock to) {
        DictionaryUtils.GetOrAddList(neighbors, from.Id).Add(to);
    }

    private static List<CfgBlock> GetSyntheticEntryBlocks(HashSet<CfgBlock> component, CfgPartitionEdgeIndex edgeIndex, List<CfgPartitionRoot> roots) {
        HashSet<int> rootBlockIds = roots.Select(root => root.EntryBlock.Id).ToHashSet();
        // Entry blocks are those reachable from outside the component (including existing roots).
        IEnumerable<CfgBlock> candidateEntryBlocks = component
            .Where(block => rootBlockIds.Contains(block.Id) || edgeIndex.HasIncomingFromOutside(block, component));
        List<CfgBlock> candidateEntries = CfgPartitionOrdering.BlocksByAddressAndId(candidateEntryBlocks).ToList();
        if (candidateEntries.Count > 0) {
            return candidateEntries;
        }
        // Fully isolated shared component with no external predecessors; pick the lowest-address block.
        return [CfgPartitionOrdering.BlocksByAddressAndId(component).First()];
    }

}
