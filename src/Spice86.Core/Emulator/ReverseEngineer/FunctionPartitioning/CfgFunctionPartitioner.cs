namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

using SequentialIdAllocator = Spice86.Shared.Utils.SequentialIdAllocator;

/// <summary>
/// Derives conservative function and helper partitions from a full exported CFG block graph.
/// </summary>
public sealed class CfgFunctionPartitioner {
    private readonly CfgPartitionEdgeCollector _edgeCollector = new();
    private readonly CfgPartitionRootCollector _rootCollector = new();
    private readonly CfgPartitionRegionGrower _regionGrower = new();
    private readonly CfgPartitionAssembler _partitionAssembler = new();
    private readonly CfgPartitionNormalizer _partitionNormalizer = new();
    private readonly CfgPartitionEdgeAnnotator _edgeAnnotator = new();

    /// <summary>
    /// Partitions a full exported block graph. Truncated graphs must be rejected by the caller.
    /// </summary>
    internal CfgPartitionedProgram Partition(
        CfgBlockGraph graph,
        ExecutionContextManager contextManager,
        FunctionCatalogue? functionCatalogue) {
        if (graph.Blocks.Length == 0) {
            return new CfgPartitionedProgram {
                Partitions = [],
                Transfers = []
            };
        }

        List<CfgBlock> blocks = CfgPartitionOrdering
            .BlocksByAddressAndId(graph.Blocks.Select(node => node.Block))
            .ToList();
        Dictionary<int, CfgBlock> blocksById = blocks.ToDictionary(block => block.Id, block => block);
        // Step 1: classify all instruction-level edges between blocks.
        List<CfgPartitionEdgeRecord> edgeRecords = _edgeCollector.Collect(blocksById);
        CfgPartitionEdgeIndex edgeIndex = new(edgeRecords);
        // Step 2: identify partition roots from call targets, context entries, CPU faults, and unmatched ret targets.
        List<CfgPartitionRoot> roots = _rootCollector.Collect(blocks, edgeIndex, contextManager, functionCatalogue);
        EnsureHasRoot(roots);
        // Step 3: add roots for self-contained isolated loops not reachable from any current root.
        roots = _rootCollector.AddSelfContainedComponentRoots(blocks, edgeIndex, roots, functionCatalogue);
        // Step 4: flood-fill each root's owned region via ownership-preserving edges.
        Dictionary<CfgBlock, HashSet<CfgPartitionRoot>> ownersByBlock = _regionGrower.Grow(edgeIndex, roots);
        EnsureNoOwnerlessBlocks(blocks, edgeIndex, ownersByBlock);
        SequentialIdAllocator idAllocator = new(1);
        // Step 5: combine root regions and shared-block components into partition drafts.
        CfgPartitionAssignment assignment = _partitionAssembler.Assemble(blocks, edgeIndex, roots, ownersByBlock, idAllocator);
        // Step 6: split multi-entry synthetic partitions into simpler single-entry regions by dominance.
        assignment = _partitionNormalizer.Normalize(assignment, edgeIndex, idAllocator);
        List<CfgCodePartition> partitions = assignment.Partitions
            .OrderBy(partition => partition.Id)
            .Select(partition => partition.ToPartition())
            .ToList();
        Dictionary<CfgPartitionDraft, CfgCodePartition> partitionByDraft = assignment.Partitions
            .ToDictionary(partition => partition, partition => partitions.Single(result => result.Id == partition.Id));
        Dictionary<CfgBlock, CfgCodePartition> partitionByBlock = assignment.PartitionByBlock
            .ToDictionary(entry => entry.Key, entry => partitionByDraft[entry.Value]);
        // Step 7: classify inter-partition transfers (call-out, aligned return, dynamic return, jump, fault).
        List<CfgCodePartitionTransfer> transfers = _edgeAnnotator.CollectTransfers(edgeIndex, partitionByBlock);

        return new CfgPartitionedProgram {
            Partitions = partitions,
            Transfers = transfers
        };
    }

    private static void EnsureHasRoot(List<CfgPartitionRoot> roots) {
        if (roots.Count > 0) {
            return;
        }
        throw new InvalidOperationException(
            "Cannot partition a non-empty CFG block graph without at least one partition root. "
            + "Root evidence must come from an execution context entry, call target, or CPU fault target.");
    }

    private static void EnsureNoOwnerlessBlocks(
        List<CfgBlock> blocks,
        CfgPartitionEdgeIndex edgeIndex,
        Dictionary<CfgBlock, HashSet<CfgPartitionRoot>> ownersByBlock) {
        foreach (CfgBlock block in blocks) {
            if (ownersByBlock.ContainsKey(block)) {
                continue;
            }
            List<string> incomingEdges = edgeIndex.GetIncomingEdges(block.Id)
                .Select(edge => $"{edge.Kind} from block {edge.SourceBlock.Id} at {edge.SourceNode.Address}")
                .ToList();
            string incomingDescription = "none";
            if (incomingEdges.Count > 0) {
                incomingDescription = string.Join(", ", incomingEdges);
            }
            throw new InvalidOperationException(
                $"Cannot partition CFG block {block.Id} at {block.Entry.Address}: block is ownerless. "
                + $"Incoming partition edges: {incomingDescription}. "
                + "A complete emulator CFG export should explain every block from an execution context entry, "
                + "call target, CPU fault target, or ownership-preserving predecessor.");
        }
    }
}
