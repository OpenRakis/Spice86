namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Linq;

/// <summary>
/// Performs the reachability sweep that removes speculative nodes once a speculative node is
/// proven wrong and must be discarded.
///
/// <para>Given a speculative <see cref="CfgInstruction"/> that must be discarded, the sweep:</para>
/// <list type="number">
///   <item><description>
///     Forward-DFS from the discard root, collecting all speculative nodes reachable from it (candidate set C).
///     Stops at non-speculative nodes (observed boundary).
///   </description></item>
///   <item><description>
///     Computes the survivor set S (subset of C): nodes still reachable from a non-speculative predecessor
///     via a path that does not pass through the discard root.
///   </description></item>
///   <item><description>
///     Removes C \ S: detaches each node's edges (bidirectional), evicts its index entry, and
///     tears down block structure (fully-speculative blocks removed, mixed blocks truncated).
///   </description></item>
/// </list>
///
/// <para>Speculative in-edges to the discarded root are dropped, not redirected.</para>
/// </summary>
public class SpeculativeReachabilityPruner {
    private readonly NodeLinker _nodeLinker;
    private readonly CfgNodeIndex _nodeIndex;

    public SpeculativeReachabilityPruner(NodeLinker nodeLinker, CfgNodeIndex nodeIndex) {
        _nodeLinker = nodeLinker;
        _nodeIndex = nodeIndex;
    }

    /// <summary>
    /// Discards <paramref name="discardRoot"/> and all speculative nodes exclusively reachable
    /// through it. Returns the set of removed nodes.
    /// </summary>
    public HashSet<ICfgNode> Sweep(CfgInstruction discardRoot) {
        HashSet<ICfgNode> candidates = CollectCandidates(discardRoot);
        HashSet<ICfgNode> survivors = ComputeSurvivors(candidates, discardRoot);
        HashSet<ICfgNode> toRemove = new(candidates);
        toRemove.ExceptWith(survivors);
        RemoveNodes(toRemove);
        return toRemove;
    }

    private HashSet<ICfgNode> CollectCandidates(CfgInstruction discardRoot) {
        HashSet<ICfgNode> visited = new();
        Queue<ICfgNode> queue = new();
        queue.Enqueue(discardRoot);
        while (queue.Count > 0) {
            ICfgNode node = queue.Dequeue();
            if (!node.IsSpeculative || !visited.Add(node)) {
                continue;
            }
            foreach (ICfgNode successor in node.Successors) {
                queue.Enqueue(successor);
            }
        }
        return visited;
    }

    private HashSet<ICfgNode> ComputeSurvivors(HashSet<ICfgNode> candidates, ICfgNode discardRoot) {
        HashSet<ICfgNode> survivors = new();
        Queue<ICfgNode> queue = new();
        foreach (ICfgNode candidate in candidates) {
            if (candidate.Equals(discardRoot)) {
                continue;
            }
            foreach (ICfgNode predecessor in candidate.Predecessors) {
                if (!candidates.Contains(predecessor) && !predecessor.Equals(discardRoot)) {
                    queue.Enqueue(candidate);
                    break;
                }
            }
        }
        while (queue.Count > 0) {
            ICfgNode node = queue.Dequeue();
            if (!candidates.Contains(node) || node.Equals(discardRoot) || !survivors.Add(node)) {
                continue;
            }
            foreach (ICfgNode successor in node.Successors) {
                if (candidates.Contains(successor)) {
                    queue.Enqueue(successor);
                }
            }
        }
        return survivors;
    }

    private void RemoveNodes(HashSet<ICfgNode> toRemove) {
        foreach (ICfgNode node in toRemove) {
            TearDownBlockFor(node, toRemove);
        }
        foreach (ICfgNode node in toRemove) {
            _nodeLinker.DetachNode(node);
            if (node is CfgInstruction instr) {
                _nodeIndex.Remove(instr);
            }
        }
    }

    private void TearDownBlockFor(ICfgNode node, HashSet<ICfgNode> toRemove) {
        CfgBlock? block = node.ContainingBlock;
        if (block is null) {
            return;
        }
        bool allSpeculativeInBlock = block.Instructions.All(n => toRemove.Contains(n));
        if (allSpeculativeInBlock) {
            foreach (ICfgNode blockNode in block.Instructions) {
                blockNode.ContainingBlock = null;
            }
        } else {
            int firstSweptIndex = -1;
            for (int i = 0; i < block.Instructions.Count; i++) {
                if (toRemove.Contains(block.Instructions[i])) {
                    firstSweptIndex = i;
                    break;
                }
            }
            if (firstSweptIndex > 0) {
                _nodeLinker.TruncateBlockAt(block, firstSweptIndex);
            }
        }
    }
}
