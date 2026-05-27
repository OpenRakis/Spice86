namespace Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Intermediate;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Collects partition root candidates from execution context, call, and fault evidence.
/// </summary>
internal sealed class CfgPartitionRootCollector {
    public List<CfgPartitionRoot> Collect(
        List<CfgBlock> blocks,
        CfgPartitionEdgeIndex edgeIndex,
        ExecutionContextManager contextManager,
        FunctionCatalogue? functionCatalogue) {
        Dictionary<int, CfgPartitionRoot> rootsByBlockId = new();
        HashSet<int> includedBlockIds = blocks.Select(block => block.Id).ToHashSet();
        Dictionary<SegmentedAddress, List<CfgBlock>> blocksByEntryAddress = blocks
            .GroupBy(block => block.Entry.Address)
            .ToDictionary(group => group.Key, group => group.ToList());
        foreach (KeyValuePair<SegmentedAddress, ISet<CfgInstruction>> entryPoint in contextManager.ExecutionContextEntryPoints) {
            foreach (CfgInstruction instruction in entryPoint.Value) {
                CfgBlock? block = instruction.ContainingBlock;
                if (block != null && includedBlockIds.Contains(block.Id)) {
                    CfgPartitionRoot root = GetOrAddRoot(rootsByBlockId, block, CfgCodePartitionKind.Observed, functionCatalogue);
                    root.AddEntry(instruction, CfgCodePartitionEntryKind.ExecutionContextEntry);
                }
            }
        }

        if (functionCatalogue != null) {
            foreach (FunctionInformation functionInformation in functionCatalogue.FunctionInformations.Values) {
                if (!blocksByEntryAddress.TryGetValue(functionInformation.Address, out List<CfgBlock>? matchingBlocks)) {
                    continue;
                }
                foreach (CfgBlock block in matchingBlocks) {
                    CfgPartitionRoot root = GetOrAddRoot(rootsByBlockId, block, CfgCodePartitionKind.Observed, functionCatalogue);
                    root.AddEntry(block.Entry, CfgCodePartitionEntryKind.FunctionEntry);
                }
            }
        }

        // Only aligned continuations suppress RetTarget root promotion.
        // Misaligned continuations are not ownership-preserving, so their target blocks must
        // become roots (via RetTarget below) to avoid ownerless blocks.
        HashSet<int> callContinuationTargets = edgeIndex.EdgeRecords
            .Where(e => e.Kind == ClassifiedEdgeKind.CallContinuation)
            .Select(e => e.TargetBlock.Id)
            .ToHashSet();

        foreach (CfgPartitionEdgeRecord edge in edgeIndex.EdgeRecords) {
            if (edge.Kind == ClassifiedEdgeKind.Call) {
                CfgPartitionRoot root = GetOrAddRoot(rootsByBlockId, edge.TargetBlock, CfgCodePartitionKind.Observed, functionCatalogue);
                root.AddEntry(edge.TargetNode, CfgCodePartitionEntryKind.FunctionEntry);
            } else if (edge.Kind == ClassifiedEdgeKind.CpuFault) {
                CfgPartitionRoot root = GetOrAddRoot(rootsByBlockId, edge.TargetBlock, CfgCodePartitionKind.Observed, functionCatalogue);
                root.AddEntry(edge.TargetNode, CfgCodePartitionEntryKind.ExecutionContextEntry);
            } else if (edge.Kind == ClassifiedEdgeKind.RetTarget && !callContinuationTargets.Contains(edge.TargetBlock.Id)) {
                CfgPartitionRoot root = GetOrAddRoot(rootsByBlockId, edge.TargetBlock, CfgCodePartitionKind.Observed, functionCatalogue);
                root.AddEntry(edge.TargetNode, CfgCodePartitionEntryKind.ReturnTargetEntry);
            }
        }

        return CfgPartitionOrdering.RootsByEntryBlock(rootsByBlockId.Values).ToList();
    }

    public List<CfgPartitionRoot> AddSelfContainedComponentRoots(
        List<CfgBlock> blocks,
        CfgPartitionEdgeIndex edgeIndex,
        List<CfgPartitionRoot> roots,
        FunctionCatalogue? functionCatalogue) {
        Dictionary<int, CfgPartitionRoot> rootsByBlockId = roots.ToDictionary(root => root.EntryBlock.Id, root => root);
        List<CfgPartitionRoot> allRoots = new(roots);
        foreach (CfgBlock block in blocks) {
            if (rootsByBlockId.ContainsKey(block.Id) || edgeIndex.HasIncomingFromDifferentBlock(block)) {
                continue;
            }
            // Only self-loop edges (no external predecessor): the block is an isolated component
            // that no root's grow phase will ever reach, so it must become its own root.
            if (!edgeIndex.HasIncomingFromSameBlock(block)) {
                continue;
            }
            CfgPartitionRoot root = GetOrAddRoot(rootsByBlockId, block, CfgCodePartitionKind.Observed, functionCatalogue);
            root.AddEntry(block.Entry, CfgCodePartitionEntryKind.GraphComponentEntry);
            allRoots.Add(root);
        }
        return CfgPartitionOrdering.RootsByEntryBlock(allRoots).ToList();
    }

    private static CfgPartitionRoot GetOrAddRoot(
        Dictionary<int, CfgPartitionRoot> rootsByBlockId,
        CfgBlock block,
        CfgCodePartitionKind kind,
        FunctionCatalogue? functionCatalogue) {
        if (rootsByBlockId.TryGetValue(block.Id, out CfgPartitionRoot? root)) {
            return root;
        }
        string name = CfgPartitionNameProvider.GetPartitionName(block.Entry.Address, functionCatalogue);
        root = new CfgPartitionRoot(block, kind, name);
        rootsByBlockId.Add(block.Id, root);
        return root;
    }
}
