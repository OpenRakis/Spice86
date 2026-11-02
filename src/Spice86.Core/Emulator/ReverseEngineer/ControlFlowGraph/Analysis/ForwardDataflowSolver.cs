namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

/// <summary>Solves forward dataflow problems over the blocks of one block graph.</summary>
internal static class ForwardDataflowSolver {
    /// <summary>
    /// The in-value of a block is the join of the out-values of its predecessors in
    /// <paramref name="graph"/>, joined with <paramref name="entryValue"/> when the block is one of
    /// <see cref="BlockGraph{TBlock}.EntryBlocks"/> (<paramref name="bottom"/> when neither applies);
    /// its out-value is <paramref name="transfer"/> applied to its in-value. Iterates until no value
    /// changes, so <paramref name="transfer"/> must be monotone and the set of values finite.
    /// </summary>
    public static ForwardDataflowResult<TBlock, T> Solve<TBlock, T>(BlockGraph<TBlock> graph, DepthFirstOrdering<TBlock> traversal,
        Func<TBlock, T, T> transfer, Func<T, T, T> join, T bottom, T entryValue) where TBlock : class where T : notnull {
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        Dictionary<TBlock, T> inValues = new();
        Dictionary<TBlock, T> outValues = new();
        HashSet<TBlock> entries = [.. graph.EntryBlocks];
        foreach (TBlock block in graph.Blocks) {
            inValues[block] = bottom;
            outValues[block] = bottom;
        }
        List<TBlock> order = [.. traversal.ReversePostOrder];
        order.AddRange(graph.Blocks.Where(block => !traversal.Contains(block)));
        Queue<TBlock> worklist = new(order);
        HashSet<TBlock> queued = new(order);
        while (worklist.Count > 0) {
            TBlock block = worklist.Dequeue();
            queued.Remove(block);
            T inValue = bottom;
            foreach (TBlock predecessor in graph.GetPredecessors(block)) {
                inValue = join(inValue, outValues[predecessor]);
            }
            if (entries.Contains(block)) {
                inValue = join(inValue, entryValue);
            }
            inValues[block] = inValue;
            T outValue = transfer(block, inValue);
            if (comparer.Equals(outValue, outValues[block])) {
                continue;
            }
            outValues[block] = outValue;
            foreach (TBlock successor in graph.GetSuccessors(block).Where(queued.Add)) {
                worklist.Enqueue(successor);
            }
        }
        return new ForwardDataflowResult<TBlock, T>(inValues, outValues);
    }
}
