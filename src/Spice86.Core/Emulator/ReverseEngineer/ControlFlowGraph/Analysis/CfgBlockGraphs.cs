namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Collections.Generic;
using System.Linq;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Builds a <see cref="BlockGraph{TBlock}"/> of <see cref="CfgBlock"/> from a partition.
/// </summary>
internal static class CfgBlockGraphs {
    /// <summary>
    /// Builds a <see cref="BlockGraph{TBlock}"/> from a partition.
    /// </summary>
    /// <param name="partition">The partition.</param>
    /// <param name="primaryEntryNode">The primary entry node.</param>
    /// <returns>The block graph.</returns>
    /// <exception cref="InvalidOperationException">Partition structure invalid.</exception>
    public static BlockGraph<CfgBlock> FromPartition(CfgCodePartition partition, ICfgNode primaryEntryNode) {
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

        return new BlockGraph<CfgBlock>(
            blocks,
            entryBlocks,
            successorsByBlock.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<CfgBlock>)kvp.Value),
            fallthroughByBlock,
            CfgBlockOrder.Instance,
            block => block.Entry.Address.ToString());
    }
}
