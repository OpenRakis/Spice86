namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Tests.Utility;

/// <summary>
/// Structural invariants of the exported control-flow graph, checked on every catalogue graph so the
/// goldens they replace can go away. Each failure names the golden key, the invariant number and the
/// block id.
/// </summary>
internal static class CfgInvariants {
    /// <summary>Builds the graph of a finished run's execution context and checks it.</summary>
    public static void AssertValid(string goldenKey, ExecutionContextManager contextManager) =>
        AssertValid(goldenKey, CfgBlocksTestJson.BuildGraph(contextManager));

    /// <summary>Checks the four invariants on a graph.</summary>
    public static void AssertValid(string goldenKey, CfgCpuGraph graph) {
        CheckUniqueIds(goldenKey, graph);
        Dictionary<int, CfgBlockInfo> blocksById = graph.Blocks.ToDictionary(block => block.Id);
        CheckLinksAgree(goldenKey, blocksById);
        CheckPartitionsCover(goldenKey, graph, blocksById);
        CheckReachability(goldenKey, graph, blocksById);
    }

    /// <summary>Invariant 1: block ids are unique.</summary>
    private static void CheckUniqueIds(string goldenKey, CfgCpuGraph graph) {
        HashSet<int> ids = [];
        foreach (CfgBlockInfo block in graph.Blocks) {
            if (!ids.Add(block.Id)) {
                throw new InvalidOperationException($"{goldenKey}: invariant 1 violated, block {block.Id} has a duplicate id");
            }
        }
    }

    /// <summary>Invariant 2: links agree in both directions - <c>b.Succ</c> contains <c>c</c> if and
    /// only if <c>c.Pred</c> contains <c>b</c>.</summary>
    private static void CheckLinksAgree(string goldenKey, Dictionary<int, CfgBlockInfo> blocksById) {
        foreach ((int id, CfgBlockInfo block) in blocksById) {
            foreach (int successorId in block.Succ) {
                if (!blocksById.TryGetValue(successorId, out CfgBlockInfo? successor)
                    || !successor.Pred.Contains(id)) {
                    throw new InvalidOperationException($"{goldenKey}: invariant 2 violated, block {successorId} is a successor of block {id} but does not list it as a predecessor");
                }
            }
            foreach (int predecessorId in block.Pred) {
                if (!blocksById.TryGetValue(predecessorId, out CfgBlockInfo? predecessor)
                    || !predecessor.Succ.Contains(id)) {
                    throw new InvalidOperationException($"{goldenKey}: invariant 2 violated, block {id} lists block {predecessorId} as a predecessor that does not list it as a successor");
                }
            }
        }
    }

    /// <summary>Invariant 3: every id in a partition is a block id, and every block that is not dead
    /// appears in at least one partition.</summary>
    private static void CheckPartitionsCover(string goldenKey, CfgCpuGraph graph,
        Dictionary<int, CfgBlockInfo> blocksById) {
        if (graph.Partitions is null) {
            return;
        }
        HashSet<int> covered = [];
        foreach (CfgPartitionInfo partition in graph.Partitions) {
            foreach (int blockId in partition.Blocks) {
                if (!blocksById.ContainsKey(blockId)) {
                    throw new InvalidOperationException($"{goldenKey}: invariant 3 violated, block {blockId} is in partition {partition.Id} but is not a block id");
                }
                covered.Add(blockId);
            }
        }
        foreach (CfgBlockInfo block in graph.Blocks) {
            if (block.Dead != true && !covered.Contains(block.Id)) {
                throw new InvalidOperationException($"{goldenKey}: invariant 3 violated, block {block.Id} is in no partition");
            }
        }
    }

    /// <summary>Invariant 4: every block that is not dead is reachable through <c>Succ</c> from a
    /// block whose entry is in <see cref="CfgCpuGraph.EntryPointAddresses"/> or from a partition
    /// entry block.</summary>
    private static void CheckReachability(string goldenKey, CfgCpuGraph graph,
        Dictionary<int, CfgBlockInfo> blocksById) {
        HashSet<int> roots = [];
        foreach (CfgBlockInfo block in graph.Blocks.Where(block => graph.EntryPointAddresses.Contains(block.Entry))) {
            roots.Add(block.Id);
        }
        if (graph.Partitions is not null) {
            foreach (CfgPartitionInfo partition in graph.Partitions) {
                foreach (CfgPartitionEntryInfo entry in partition.Entries) {
                    roots.Add(entry.Block);
                }
            }
        }
        HashSet<int> reachable = [];
        Queue<int> pending = new(roots);
        while (pending.Count > 0) {
            int id = pending.Dequeue();
            if (!reachable.Add(id) || !blocksById.TryGetValue(id, out CfgBlockInfo? block)) {
                continue;
            }
            foreach (int successorId in block.Succ) {
                pending.Enqueue(successorId);
            }
        }
        foreach (CfgBlockInfo block in graph.Blocks) {
            if (block.Dead != true && !reachable.Contains(block.Id)) {
                throw new InvalidOperationException($"{goldenKey}: invariant 4 violated, block {block.Id} is not reachable from an entry point or a partition entry");
            }
        }
    }
}
