namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Encapsulates the promote-or-sweep reconciliation decision for speculative nodes.
/// When a pre-existing speculative node at a given address is encountered during execution,
/// we compare its signature to the live memory signature:
/// if they match the speculative node is promoted in place; otherwise it is swept (pruned)
/// and the address is poisoned to prevent future speculation there.
/// </summary>
public class SpeculativeReconciler {
    private readonly SpeculativePromoter _promoter;
    private readonly SpeculativeReachabilityPruner _pruner;
    private readonly CfgNodeIndex _nodeIndex;

    public SpeculativeReconciler(SpeculativePromoter promoter, SpeculativeReachabilityPruner pruner, CfgNodeIndex nodeIndex) {
        _promoter = promoter;
        _pruner = pruner;
        _nodeIndex = nodeIndex;
    }

    /// <summary>
    /// Reconciles a pre-existing speculative node with the live memory state.
    /// Returns <c>true</c> if the node was promoted (caller should use it directly).
    /// Returns <c>false</c> if the node was swept and the address poisoned (caller should use
    /// the live replacement node instead).
    /// </summary>
    /// <param name="speculativeNode">The speculative node currently in the graph at the address.</param>
    /// <param name="liveSignature">The signature from the live (parsed/observed) instruction at the same address.</param>
    /// <param name="address">The segmented address being reconciled.</param>
    public bool Reconcile(CfgInstruction speculativeNode, Signature liveSignature, SegmentedAddress address) {
        if (speculativeNode.Signature.ListEquivalent(liveSignature.SignatureValue)) {
            _promoter.Promote(speculativeNode);
            return true;
        }
        _pruner.Sweep(speculativeNode);
        _nodeIndex.PoisonSet.Add(address);
        return false;
    }
}
