namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

/// <summary>Solves backward dataflow problems (such as liveness) over the blocks of one block graph.</summary>
internal static class BackwardDataflowSolver {
    /// <summary>
    /// The live-out value of a block is the join of the live-in values of its successors in
    /// <paramref name="graph"/> (<paramref name="bottom"/> when it has none); its live-in value is
    /// <paramref name="transfer"/> applied to its live-out value. Iterates until no value changes, so
    /// <paramref name="transfer"/> must be monotone and the set of values finite.
    /// </summary>
    public static BackwardDataflowResult<TBlock, T> Solve<TBlock, T>(BlockGraph<TBlock> graph, DepthFirstOrdering<TBlock> traversal,
        Func<TBlock, T, T> transfer, Func<T, T, T> join, T bottom) where TBlock : class where T : notnull {
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        Dictionary<TBlock, T> liveIn = new();
        Dictionary<TBlock, T> liveOut = new();
        foreach (TBlock block in graph.Blocks) {
            liveIn[block] = bottom;
            liveOut[block] = bottom;
        }
        List<TBlock> order = [.. traversal.PostOrder];
        order.AddRange(graph.Blocks.Where(block => !traversal.Contains(block)));
        Queue<TBlock> worklist = new(order);
        HashSet<TBlock> queued = new(order);
        while (worklist.Count > 0) {
            TBlock block = worklist.Dequeue();
            queued.Remove(block);
            T outValue = bottom;
            foreach (TBlock successor in graph.GetSuccessors(block)) {
                outValue = join(outValue, liveIn[successor]);
            }
            liveOut[block] = outValue;
            T inValue = transfer(block, outValue);
            if (comparer.Equals(inValue, liveIn[block])) {
                continue;
            }
            liveIn[block] = inValue;
            foreach (TBlock predecessor in graph.GetPredecessors(block).Where(queued.Add)) {
                worklist.Enqueue(predecessor);
            }
        }
        return new BackwardDataflowResult<TBlock, T>(liveIn, liveOut);
    }
}
