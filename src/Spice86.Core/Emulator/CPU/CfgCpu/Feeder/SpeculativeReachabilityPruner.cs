namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

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
///     Removes C \ S by fanning each node out through <see cref="InstructionReplacerRegistry.RemoveInstruction"/>,
///     so every subscriber (index, linker/block teardown, entry-point roots, memory caches) cleans up its own state.
///   </description></item>
/// </list>
///
/// <para>Speculative in-edges to the discarded root are dropped, not redirected.</para>
/// </summary>
public class SpeculativeReachabilityPruner {
    private readonly InstructionReplacerRegistry _replacerRegistry;

    public SpeculativeReachabilityPruner(InstructionReplacerRegistry replacerRegistry) {
        _replacerRegistry = replacerRegistry;
    }

    /// <summary>
    /// Discards <paramref name="discardRoot"/> and all speculative nodes exclusively reachable
    /// through it. Returns the set of removed nodes.
    /// </summary>
    public HashSet<ICfgNode> Sweep(CfgInstruction discardRoot) {
        // Collect all potentially doomed speculative nodes reachable from the discard root, stopping at non-speculative nodes.
        HashSet<ICfgNode> candidates = CollectCandidates(discardRoot);
        // Collect the survivors: candidates that are still reachable from outside the doomed subgraph (without passing through discardRoot).
        HashSet<ICfgNode> survivors = ComputeSurvivors(candidates, discardRoot);
        // Remove the doomed nodes (all candidates except survivors) from the CFG and all other registries.
        HashSet<ICfgNode> toRemove = new(candidates);
        toRemove.ExceptWith(survivors);
        RemoveNodes(toRemove);
        return toRemove;
    }

    private static HashSet<ICfgNode> CollectCandidates(CfgInstruction discardRoot) {
        // All speculative nodes reachable from the discard root, they are the potentially doomed set.
        return BreadthFirstSearch.Enumerate(
            (ICfgNode)discardRoot,
            node => node.Successors.Where(successor => successor.IsSpeculative))
            .ToHashSet();
    }

    private static HashSet<ICfgNode> ComputeSurvivors(HashSet<ICfgNode> candidates, ICfgNode discardRoot) {
        bool HasExternalPredecessor(ICfgNode node) =>
            node.Predecessors.Any(predecessor => !candidates.Contains(predecessor) && !predecessor.Equals(discardRoot));

        bool IsEligibleSuccessor(ICfgNode node) =>
            candidates.Contains(node) && !node.Equals(discardRoot);

        // A candidate survives if it has an incoming edge from outside the doomed set,
        // meaning it is still reachable without going through the discard root.
        IEnumerable<ICfgNode> seeds = candidates
            .Where(candidate => !candidate.Equals(discardRoot) && HasExternalPredecessor(candidate));
        // Flood-fill forward from those survivors to rescue anything reachable through them.
        return BreadthFirstSearch.Enumerate(
            seeds,
            node => node.Successors.Where(IsEligibleSuccessor))
            .ToHashSet();
    }

    private void RemoveNodes(HashSet<ICfgNode> toRemove) {
        foreach (CfgInstruction instruction in toRemove.OfType<CfgInstruction>()) {
            _replacerRegistry.RemoveInstruction(instruction);
        }
    }
}
