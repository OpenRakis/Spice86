namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

/// <summary>Solves backward dataflow problems (such as liveness) over the blocks of one partition.</summary>
internal static class BackwardDataflowSolver {
    /// <summary>
    /// The live-out value of a block is the join of the live-in values of its successors in
    /// <paramref name="graph"/> (<paramref name="bottom"/> when it has none); its live-in value is
    /// <paramref name="transfer"/> applied to its live-out value. Iterates until no value changes, so
    /// <paramref name="transfer"/> must be monotone and the set of values finite.
    /// </summary>
    public static BackwardDataflowResult<T> Solve<T>(PartitionBlockGraph graph, DepthFirstOrdering<CfgBlock> traversal,
        Func<CfgBlock, T, T> transfer, Func<T, T, T> join, T bottom) where T : notnull {
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        Dictionary<CfgBlock, T> liveIn = new();
        Dictionary<CfgBlock, T> liveOut = new();
        foreach (CfgBlock block in graph.Blocks) {
            liveIn[block] = bottom;
            liveOut[block] = bottom;
        }
        List<CfgBlock> order = [.. traversal.PostOrder];
        order.AddRange(graph.Blocks.Where(block => !traversal.Contains(block)));
        Queue<CfgBlock> worklist = new(order);
        HashSet<CfgBlock> queued = new(order);
        while (worklist.Count > 0) {
            CfgBlock block = worklist.Dequeue();
            queued.Remove(block);
            T outValue = bottom;
            foreach (CfgBlock successor in graph.GetSuccessors(block)) {
                outValue = join(outValue, liveIn[successor]);
            }
            liveOut[block] = outValue;
            T inValue = transfer(block, outValue);
            if (comparer.Equals(inValue, liveIn[block])) {
                continue;
            }
            liveIn[block] = inValue;
            foreach (CfgBlock predecessor in graph.GetPredecessors(block).Where(queued.Add)) {
                worklist.Enqueue(predecessor);
            }
        }
        return new BackwardDataflowResult<T>(liveIn, liveOut);
    }
}
