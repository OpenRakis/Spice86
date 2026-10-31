namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using System.Linq;

/// <summary>
/// Dominance frontiers of a block graph: the blocks where a definition's dominance ends, computed
/// Cooper-Cytron style by walking from each join's predecessors up to its immediate dominator. An
/// entry block that also has predecessors counts as a join.
/// </summary>
internal static class DominanceFrontiers {
    /// <summary>The dominance frontier of every block of <paramref name="graph"/> under <paramref name="tree"/>.</summary>
    public static Dictionary<TBlock, HashSet<TBlock>> Compute<TBlock>(BlockGraph<TBlock> graph,
        DominatorTree<TBlock> tree) where TBlock : class {
        HashSet<TBlock> entries = graph.EntryBlocks.ToHashSet();
        Dictionary<TBlock, HashSet<TBlock>> frontiers = graph.Blocks
            .ToDictionary(block => block, _ => new HashSet<TBlock>());
        foreach (TBlock block in graph.Blocks) {
            IReadOnlyList<TBlock> blockPredecessors = graph.PredecessorsByBlock[block];
            if (blockPredecessors.Count < 2 && !(entries.Contains(block) && blockPredecessors.Count >= 1)) {
                continue;
            }
            TBlock? stop = tree.GetImmediateDominator(block);
            foreach (TBlock predecessor in blockPredecessors) {
                for (TBlock? runner = predecessor;
                    runner is not null && !ReferenceEquals(runner, stop);
                    runner = tree.GetImmediateDominator(runner)) {
                    frontiers[runner].Add(block);
                }
            }
        }
        return frontiers;
    }
}
