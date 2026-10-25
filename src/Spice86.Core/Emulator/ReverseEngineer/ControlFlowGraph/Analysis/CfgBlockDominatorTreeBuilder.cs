namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

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

    /// <summary>
    /// Cooper-Harvey-Kennedy on post-order numbers. A virtual root (number <c>n</c>) parents the entry
    /// blocks, so a block whose only common dominator is the virtual root, for example the join of two
    /// entries, gets no immediate dominator.
    /// </summary>
    public CfgBlockDominatorTree BuildFromEntries(
        IReadOnlyCollection<CfgBlock> blocks,
        IReadOnlyCollection<CfgBlock> entryBlocks,
        IReadOnlyDictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock) {
        HashSet<CfgBlock> graphBlocks = blocks.ToHashSet();
        HashSet<CfgBlock> entryBlockSet = entryBlocks.Where(graphBlocks.Contains).ToHashSet();
        List<CfgBlock> reachableBlocks = FindReachableBlocks(graphBlocks, entryBlocks, successorsByBlock);
        Dictionary<CfgBlock, List<CfgBlock>> predecessorsByBlock = BuildPredecessors(graphBlocks, reachableBlocks, successorsByBlock);

        IEnumerable<CfgBlock> roots = entryBlocks
            .Where(graphBlocks.Contains)
            .OrderBy(block => block.Entry.Address)
            .ThenBy(block => block.Id);
        DepthFirstOrdering<CfgBlock> ordering = new(roots, block => GetSuccessorsInGraph(block, graphBlocks, successorsByBlock));
        int n = ordering.PostOrder.Count;
        int[] idom = new int[n + 1];
        Array.Fill(idom, -1);
        idom[n] = n;
        foreach (CfgBlock entry in entryBlockSet) {
            idom[ordering.PostOrderIndexByNode[entry]] = n;
        }

        bool changed = true;
        while (changed) {
            changed = false;
            foreach (CfgBlock block in ordering.ReversePostOrder) {
                int blockNumber = ordering.PostOrderIndexByNode[block];
                if (idom[blockNumber] == n) {
                    continue;
                }
                List<int> readyPredecessors = predecessorsByBlock[block]
                    .Select(predecessor => ordering.PostOrderIndexByNode[predecessor])
                    .Where(predecessorNumber => idom[predecessorNumber] != -1)
                    .ToList();
                if (readyPredecessors.Count == 0) {
                    continue;
                }
                int newIdom = readyPredecessors[0];
                foreach (int predecessorNumber in readyPredecessors.Skip(1)) {
                    newIdom = Intersect(idom, predecessorNumber, newIdom);
                }
                if (idom[blockNumber] != newIdom) {
                    idom[blockNumber] = newIdom;
                    changed = true;
                }
            }
        }

        Dictionary<CfgBlock, CfgBlock?> immediateDominatorByBlock = new();
        foreach (CfgBlock block in reachableBlocks) {
            int blockNumber = ordering.PostOrderIndexByNode[block];
            int dominatorNumber = idom[blockNumber];
            immediateDominatorByBlock.Add(block, dominatorNumber == n ? null : ordering.PostOrder[dominatorNumber]);
        }
        return new CfgBlockDominatorTree(reachableBlocks, immediateDominatorByBlock);
    }

    private static int Intersect(int[] idom, int a, int b) {
        while (a != b) {
            while (a < b) {
                a = idom[a];
            }
            while (b < a) {
                b = idom[b];
            }
        }
        return a;
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
}
