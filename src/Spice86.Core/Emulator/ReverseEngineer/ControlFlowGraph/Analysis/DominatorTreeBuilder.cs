namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

/// <summary>
/// Builds dominator trees for block graphs.
/// </summary>
internal static class DominatorTreeBuilder {
    /// <summary>
    /// Builds the dominator tree of the blocks <paramref name="startBlock"/> reaches.
    /// </summary>
    public static DominatorTree<TBlock> Build<TBlock>(
        IReadOnlyCollection<TBlock> blocks,
        TBlock startBlock,
        IReadOnlyDictionary<TBlock, IReadOnlyList<TBlock>> successorsByBlock,
        IComparer<TBlock> stableOrder) where TBlock : class =>
        BuildFromEntries(blocks, [startBlock], successorsByBlock, stableOrder);

    /// <summary>
    /// Builds the dominator tree of a block graph, using the graph's blocks, entries, successors and order.
    /// </summary>
    /// <param name="graph">The block graph.</param>
    public static DominatorTree<TBlock> BuildFromEntries<TBlock>(BlockGraph<TBlock> graph) where TBlock : class =>
        BuildFromEntries(graph.Blocks, graph.EntryBlocks, graph.SuccessorsByBlock, graph.StableOrder);

    /// <summary>
    /// Cooper-Harvey-Kennedy on post-order numbers. A virtual root (number <c>n</c>) parents the entry
    /// blocks, so a block whose only common dominator is the virtual root, for example the join of two
    /// entries, gets no immediate dominator.
    /// </summary>
    public static DominatorTree<TBlock> BuildFromEntries<TBlock>(
        IReadOnlyCollection<TBlock> blocks,
        IReadOnlyCollection<TBlock> entryBlocks,
        IReadOnlyDictionary<TBlock, IReadOnlyList<TBlock>> successorsByBlock,
        IComparer<TBlock> stableOrder) where TBlock : class {
        HashSet<TBlock> graphBlocks = blocks.ToHashSet();
        HashSet<TBlock> entryBlockSet = entryBlocks.Where(graphBlocks.Contains).ToHashSet();
        List<TBlock> reachableBlocks = FindReachableBlocks(graphBlocks, entryBlocks, successorsByBlock, stableOrder);
        Dictionary<TBlock, List<TBlock>> predecessorsByBlock = BuildPredecessors(graphBlocks, reachableBlocks, successorsByBlock);

        IEnumerable<TBlock> roots = entryBlocks
            .Where(graphBlocks.Contains)
            .OrderBy(block => block, stableOrder);
        DepthFirstOrdering<TBlock> ordering = new(roots, block => GetSuccessorsInGraph(block, graphBlocks, successorsByBlock, stableOrder));
        int n = ordering.PostOrder.Count;
        int[] idom = new int[n + 1];
        Array.Fill(idom, -1);
        idom[n] = n;
        foreach (TBlock entry in entryBlockSet) {
            idom[ordering.PostOrderIndexByNode[entry]] = n;
        }

        bool changed = true;
        while (changed) {
            changed = false;
            foreach (TBlock block in ordering.ReversePostOrder) {
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

        Dictionary<TBlock, TBlock?> immediateDominatorByBlock = new();
        foreach (TBlock block in reachableBlocks) {
            int blockNumber = ordering.PostOrderIndexByNode[block];
            int dominatorNumber = idom[blockNumber];
            immediateDominatorByBlock.Add(block, dominatorNumber == n ? null : ordering.PostOrder[dominatorNumber]);
        }
        return new DominatorTree<TBlock>(reachableBlocks, immediateDominatorByBlock);
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

    private static List<TBlock> FindReachableBlocks<TBlock>(
        HashSet<TBlock> graphBlocks,
        IReadOnlyCollection<TBlock> entryBlocks,
        IReadOnlyDictionary<TBlock, IReadOnlyList<TBlock>> successorsByBlock,
        IComparer<TBlock> stableOrder) where TBlock : class {
        IEnumerable<TBlock> seeds = entryBlocks
            .Where(graphBlocks.Contains)
            .OrderBy(block => block, stableOrder);
        IEnumerable<TBlock> reachable = DepthFirstSearch.Enumerate(
            seeds,
            block => GetSuccessorsInGraph(block, graphBlocks, successorsByBlock, stableOrder));
        return reachable
            .OrderBy(block => block, stableOrder)
            .ToList();
    }

    private static IEnumerable<TBlock> GetSuccessorsInGraph<TBlock>(
        TBlock block,
        HashSet<TBlock> graphBlocks,
        IReadOnlyDictionary<TBlock, IReadOnlyList<TBlock>> successorsByBlock,
        IComparer<TBlock> stableOrder) where TBlock : class {
        if (!successorsByBlock.TryGetValue(block, out IReadOnlyList<TBlock>? successors)) {
            return [];
        }
        return successors
            .Where(graphBlocks.Contains)
            .OrderBy(successor => successor, stableOrder);
    }

    private static Dictionary<TBlock, List<TBlock>> BuildPredecessors<TBlock>(
        HashSet<TBlock> graphBlocks,
        List<TBlock> reachableBlocks,
        IReadOnlyDictionary<TBlock, IReadOnlyList<TBlock>> successorsByBlock) where TBlock : class {
        HashSet<TBlock> reachableBlockSet = reachableBlocks.ToHashSet();
        Dictionary<TBlock, List<TBlock>> predecessorsByBlock = reachableBlocks.ToDictionary(block => block, _ => new List<TBlock>());
        foreach (TBlock block in reachableBlocks) {
            if (!successorsByBlock.TryGetValue(block, out IReadOnlyList<TBlock>? successors)) {
                continue;
            }
            foreach (TBlock successor in successors) {
                if (!graphBlocks.Contains(successor) || !reachableBlockSet.Contains(successor)) {
                    continue;
                }
                predecessorsByBlock[successor].Add(block);
            }
        }
        return predecessorsByBlock;
    }
}
