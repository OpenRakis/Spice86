namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Collections.Generic;
using System.Linq;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The block-level view of one partition: blocks, entries, same-partition successors and
/// predecessors, and fallthrough successors. Feeds <see cref="CfgBlockDominatorTreeBuilder.BuildFromEntries"/>.
/// </summary>
internal sealed class PartitionBlockGraph {
    private readonly Dictionary<CfgBlock, CfgBlock?> _fallthroughSuccessorByBlock;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionBlockGraph"/> class.
    /// </summary>
    /// <param name="blocks">All blocks, kept in the given order.</param>
    /// <param name="entryBlocks">Entry blocks, index 0 = primary.</param>
    /// <param name="successorsByBlock">Successor relation.</param>
    /// <param name="fallthroughSuccessorByBlock">Fallthrough relation.</param>
    /// <exception cref="ArgumentException">Validation failed.</exception>
    internal PartitionBlockGraph(
        IReadOnlyList<CfgBlock> blocks,
        IReadOnlyList<CfgBlock> entryBlocks,
        IReadOnlyDictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock,
        IReadOnlyDictionary<CfgBlock, CfgBlock?> fallthroughSuccessorByBlock) {
        HashSet<CfgBlock> blockSet = new HashSet<CfgBlock>();
        foreach (CfgBlock block in blocks) {
            if (!blockSet.Add(block)) {
                throw new ArgumentException($"Duplicate block: {block.Entry.Address}");
            }
        }

        if (entryBlocks.Count == 0) {
            throw new ArgumentException("Entry blocks cannot be empty");
        }
        foreach (CfgBlock entryBlock in entryBlocks) {
            if (!blockSet.Contains(entryBlock)) {
                throw new ArgumentException($"Entry block not in blocks: {entryBlock.Entry.Address}");
            }
        }

        foreach (KeyValuePair<CfgBlock, IReadOnlyList<CfgBlock>> kvp in successorsByBlock) {
            if (!blockSet.Contains(kvp.Key)) {
                throw new ArgumentException($"Successor key not in blocks: {kvp.Key.Entry.Address}");
            }
            HashSet<CfgBlock> seen = new HashSet<CfgBlock>();
            foreach (CfgBlock succ in kvp.Value) {
                if (!blockSet.Contains(succ)) {
                    throw new ArgumentException($"Successor not in blocks: {succ.Entry.Address}");
                }
                if (!seen.Add(succ)) {
                    throw new ArgumentException($"Duplicate successor: {succ.Entry.Address}");
                }
            }
        }

        foreach (KeyValuePair<CfgBlock, CfgBlock?> kvp in fallthroughSuccessorByBlock) {
            if (!blockSet.Contains(kvp.Key)) {
                throw new ArgumentException($"Fallthrough key not in blocks: {kvp.Key.Entry.Address}");
            }
            if (kvp.Value != null) {
                if (!successorsByBlock.TryGetValue(kvp.Key, out IReadOnlyList<CfgBlock>? succs) || !succs.Contains(kvp.Value)) {
                    throw new ArgumentException($"Fallthrough not in successors: {kvp.Value.Entry.Address}");
                }
            }
        }

        Blocks = blocks.ToList();
        EntryBlocks = entryBlocks.ToList();
        PrimaryEntryBlock = EntryBlocks[0];

        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> succDict = new Dictionary<CfgBlock, IReadOnlyList<CfgBlock>>();
        foreach (CfgBlock block in Blocks) {
            if (successorsByBlock.TryGetValue(block, out IReadOnlyList<CfgBlock>? list)) {
                succDict[block] = list.ToList();
            } else {
                succDict[block] = new List<CfgBlock>();
            }
        }
        SuccessorsByBlock = succDict;

        Dictionary<CfgBlock, List<CfgBlock>> predDict = new Dictionary<CfgBlock, List<CfgBlock>>();
        foreach (CfgBlock block in Blocks) {
            predDict[block] = new List<CfgBlock>();
        }
        foreach (CfgBlock block in Blocks) {
            foreach (CfgBlock succ in SuccessorsByBlock[block]) {
                predDict[succ].Add(block);
            }
        }
        PredecessorsByBlock = predDict.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<CfgBlock>)kvp.Value);

        _fallthroughSuccessorByBlock = new Dictionary<CfgBlock, CfgBlock?>();
        foreach (CfgBlock block in Blocks) {
            _fallthroughSuccessorByBlock[block] = fallthroughSuccessorByBlock.TryGetValue(block, out CfgBlock? fb) ? fb : null;
        }
    }

    /// <summary>
    /// All blocks, in the order given to the constructor (address order when built by FromPartition).
    /// </summary>
    public IReadOnlyList<CfgBlock> Blocks { get; }

    /// <summary>
    /// Entry blocks; index 0 is the primary entry, the others follow in the given order.
    /// </summary>
    public IReadOnlyList<CfgBlock> EntryBlocks { get; }

    /// <summary>
    /// Primary entry block (EntryBlocks[0]).
    /// </summary>
    public CfgBlock PrimaryEntryBlock { get; }

    /// <summary>
    /// Successor relation, one key per block; each list keeps the order given to the constructor (address order when built by <see cref="FromPartition"/>).
    /// </summary>
    public IReadOnlyDictionary<CfgBlock, IReadOnlyList<CfgBlock>> SuccessorsByBlock { get; }

    /// <summary>
    /// Predecessor relation, one key per block, in Blocks order.
    /// </summary>
    public IReadOnlyDictionary<CfgBlock, IReadOnlyList<CfgBlock>> PredecessorsByBlock { get; }

    /// <summary>
    /// Gets the successors of a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The successors, in the order of <see cref="SuccessorsByBlock"/>.</returns>
    /// <exception cref="KeyNotFoundException">Block not in this graph.</exception>
    public IReadOnlyList<CfgBlock> GetSuccessors(CfgBlock block) => SuccessorsByBlock[block];

    /// <summary>
    /// Gets the predecessors of a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The predecessors in Blocks order.</returns>
    /// <exception cref="KeyNotFoundException">Block not in this graph.</exception>
    public IReadOnlyList<CfgBlock> GetPredecessors(CfgBlock block) => PredecessorsByBlock[block];

    /// <summary>
    /// Gets the fallthrough successor of a block.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The fallthrough successor or null.</returns>
    /// <exception cref="KeyNotFoundException">Block not in this graph.</exception>
    public CfgBlock? GetFallthroughSuccessor(CfgBlock block) => _fallthroughSuccessorByBlock[block];

    /// <summary>
    /// Builds a <see cref="PartitionBlockGraph"/> from a partition.
    /// </summary>
    /// <param name="partition">The partition.</param>
    /// <param name="primaryEntryNode">The primary entry node.</param>
    /// <returns>The block graph.</returns>
    /// <exception cref="InvalidOperationException">Partition structure invalid.</exception>
    public static PartitionBlockGraph FromPartition(CfgCodePartition partition, ICfgNode primaryEntryNode) {
        List<CfgBlock> blocks = partition.Blocks
            .OrderBy(b => b.Entry.Address.Linear)
            .ThenBy(b => b.Id)
            .ToList();

        Dictionary<ICfgNode, CfgBlock> nodeToBlock = new Dictionary<ICfgNode, CfgBlock>();
        foreach (CfgBlock block in blocks) {
            foreach (ICfgNode node in block.Instructions) {
                if (nodeToBlock.TryGetValue(node, out CfgBlock? existing) && existing != block) {
                    throw new InvalidOperationException($"Node {node.Address} found in multiple blocks");
                }
                nodeToBlock[node] = block;
            }
        }

        Dictionary<CfgBlock, List<CfgBlock>> successorsByBlock = new Dictionary<CfgBlock, List<CfgBlock>>();
        foreach (CfgBlock block in blocks) {
            HashSet<CfgBlock> succSet = new HashSet<CfgBlock>();
            foreach (ICfgNode succNode in block.Successors) {
                if (!nodeToBlock.TryGetValue(succNode, out CfgBlock? targetBlock)) {
                    continue;
                }
                if (targetBlock.Entry.Id != succNode.Id) {
                    throw new InvalidOperationException(
                        $"Successor {succNode.Address} of block {block.Entry.Address} is inside block {targetBlock.Entry.Address} but is not its entry.");
                }
                succSet.Add(targetBlock);
            }
            successorsByBlock[block] = succSet
                .OrderBy(b => b.Entry.Address.Linear)
                .ThenBy(b => b.Id)
                .ToList();
        }

        Dictionary<CfgBlock, CfgBlock?> fallthroughByBlock = new Dictionary<CfgBlock, CfgBlock?>();
        foreach (CfgBlock block in blocks) {
            CfgBlock? fallthrough = null;
            if (block.Terminator is CfgInstruction instr) {
                SegmentedAddress nextAddr = instr.NextInMemoryAddress32.ToSegmentedAddress();
                foreach (CfgBlock succ in successorsByBlock[block]) {
                    if (succ.Entry.Address == nextAddr) {
                        fallthrough = succ;
                        break;
                    }
                }
            }
            fallthroughByBlock[block] = fallthrough;
        }

        if (!nodeToBlock.TryGetValue(primaryEntryNode, out CfgBlock? primaryEntryBlock)) {
            throw new InvalidOperationException($"Primary entry node {primaryEntryNode.Address} not in partition");
        }
        List<CfgBlock> otherEntryBlocks = [];
        foreach (CfgCodePartitionEntry entry in partition.Entries) {
            if (!nodeToBlock.TryGetValue(entry.Node, out CfgBlock? entryBlock)) {
                throw new InvalidOperationException($"Entry node {entry.Node.Address} not in partition");
            }
            if (entryBlock != primaryEntryBlock && !otherEntryBlocks.Contains(entryBlock)) {
                otherEntryBlocks.Add(entryBlock);
            }
        }
        List<CfgBlock> entryBlocks = [primaryEntryBlock, .. otherEntryBlocks.OrderBy(b => b.Entry.Address.Linear).ThenBy(b => b.Id)];

        return new PartitionBlockGraph(
            blocks,
            entryBlocks,
            successorsByBlock.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<CfgBlock>)kvp.Value),
            fallthroughByBlock);
    }
}
