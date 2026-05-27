namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Diagnostics;
using System.Linq;

/// <summary>
/// Builds dominator trees for CFG block graphs.
/// </summary>
internal sealed class CfgBlockDominatorTreeBuilder {
    public CfgBlockDominatorTree Build(
        IReadOnlyCollection<CfgBlock> blocks,
        CfgBlock startBlock,
        IReadOnlyDictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock) =>
        BuildFromEntries(blocks, [startBlock], successorsByBlock);

    public CfgBlockDominatorTree BuildFromEntries(
        IReadOnlyCollection<CfgBlock> blocks,
        IReadOnlyCollection<CfgBlock> entryBlocks,
        IReadOnlyDictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock) {
        HashSet<CfgBlock> graphBlocks = blocks.ToHashSet();
        HashSet<CfgBlock> entryBlockSet = entryBlocks.Where(graphBlocks.Contains).ToHashSet();
        List<CfgBlock> reachableBlocks = FindReachableBlocks(graphBlocks, entryBlocks, successorsByBlock);
        Dictionary<CfgBlock, HashSet<CfgBlock>> dominatorsByBlock = InitializeDominators(reachableBlocks, entryBlockSet);
        Dictionary<CfgBlock, List<CfgBlock>> predecessorsByBlock = BuildPredecessors(graphBlocks, reachableBlocks, successorsByBlock);

        bool changed = true;
        while (changed) {
            changed = false;
            foreach (CfgBlock block in reachableBlocks) {
                HashSet<CfgBlock> updatedDominators = BuildDominatorsForBlock(block, entryBlockSet, predecessorsByBlock, dominatorsByBlock);
                if (dominatorsByBlock[block].SetEquals(updatedDominators)) {
                    continue;
                }
                dominatorsByBlock[block] = updatedDominators;
                changed = true;
            }
        }

        IReadOnlyDictionary<CfgBlock, CfgBlock?> immediateDominatorByBlock = BuildImmediateDominators(reachableBlocks, dominatorsByBlock);
        return new CfgBlockDominatorTree(reachableBlocks, dominatorsByBlock, immediateDominatorByBlock);
    }

    private static List<CfgBlock> FindReachableBlocks(
        HashSet<CfgBlock> graphBlocks,
        IReadOnlyCollection<CfgBlock> entryBlocks,
        IReadOnlyDictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock) {
        IEnumerable<CfgBlock> seeds = entryBlocks
            .Where(graphBlocks.Contains)
            .OrderBy(block => block.Entry.Address)
            .ThenBy(block => block.Id);
        IEnumerable<CfgBlock> reachable = DepthFirstSearch.Enumerate(
            seeds,
            block => GetSuccessorsInGraph(block, graphBlocks, successorsByBlock));
        return reachable
            .OrderBy(block => block.Entry.Address)
            .ThenBy(block => block.Id)
            .ToList();
    }

    private static IEnumerable<CfgBlock> GetSuccessorsInGraph(
        CfgBlock block,
        HashSet<CfgBlock> graphBlocks,
        IReadOnlyDictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock) {
        if (!successorsByBlock.TryGetValue(block, out IReadOnlyList<CfgBlock>? successors)) {
            return [];
        }
        return successors
            .Where(graphBlocks.Contains)
            .OrderBy(successor => successor.Entry.Address)
            .ThenBy(successor => successor.Id);
    }

    private static Dictionary<CfgBlock, HashSet<CfgBlock>> InitializeDominators(List<CfgBlock> reachableBlocks, HashSet<CfgBlock> entryBlocks) {
        HashSet<CfgBlock> allBlocks = reachableBlocks.ToHashSet();
        Dictionary<CfgBlock, HashSet<CfgBlock>> dominatorsByBlock = new();
        foreach (CfgBlock block in reachableBlocks) {
            HashSet<CfgBlock> dominators = entryBlocks.Contains(block) ? [block] : allBlocks.ToHashSet();
            dominatorsByBlock.Add(block, dominators);
        }
        return dominatorsByBlock;
    }

    private static Dictionary<CfgBlock, List<CfgBlock>> BuildPredecessors(
        HashSet<CfgBlock> graphBlocks,
        List<CfgBlock> reachableBlocks,
        IReadOnlyDictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock) {
        HashSet<CfgBlock> reachableBlockSet = reachableBlocks.ToHashSet();
        Dictionary<CfgBlock, List<CfgBlock>> predecessorsByBlock = reachableBlocks.ToDictionary(block => block, _ => new List<CfgBlock>());
        foreach (CfgBlock block in reachableBlocks) {
            if (!successorsByBlock.TryGetValue(block, out IReadOnlyList<CfgBlock>? successors)) {
                continue;
            }
            foreach (CfgBlock successor in successors) {
                if (!graphBlocks.Contains(successor) || !reachableBlockSet.Contains(successor)) {
                    continue;
                }
                predecessorsByBlock[successor].Add(block);
            }
        }
        return predecessorsByBlock;
    }

    private static HashSet<CfgBlock> BuildDominatorsForBlock(
        CfgBlock block,
        HashSet<CfgBlock> entryBlocks,
        Dictionary<CfgBlock, List<CfgBlock>> predecessorsByBlock,
        Dictionary<CfgBlock, HashSet<CfgBlock>> dominatorsByBlock) {
        if (entryBlocks.Contains(block)
            || !predecessorsByBlock.TryGetValue(block, out List<CfgBlock>? predecessors)
            || predecessors.Count == 0) {
            return [block];
        }
        HashSet<CfgBlock> updatedDominators = dominatorsByBlock[predecessors[0]].ToHashSet();
        foreach (CfgBlock predecessor in predecessors.Skip(1)) {
            updatedDominators.IntersectWith(dominatorsByBlock[predecessor]);
        }
        updatedDominators.Add(block);
        return updatedDominators;
    }

    private static IReadOnlyDictionary<CfgBlock, CfgBlock?> BuildImmediateDominators(
        List<CfgBlock> reachableBlocks,
        Dictionary<CfgBlock, HashSet<CfgBlock>> dominatorsByBlock) {
        Dictionary<CfgBlock, CfgBlock?> result = new();
        foreach (CfgBlock block in reachableBlocks) {
            List<CfgBlock> strictDominators = dominatorsByBlock[block]
                .Where(dominator => !dominator.Equals(block))
                .ToList();
            CfgBlock? immediateDominator = strictDominators
                .OrderByDescending(candidate => dominatorsByBlock[candidate].Count)
                .FirstOrDefault();
            Debug.Assert(strictDominators.Count == 0 || immediateDominator != null, "Immediate dominator must exist when strict dominators are present");
            result.Add(block, immediateDominator);
        }
        return result;
    }
}