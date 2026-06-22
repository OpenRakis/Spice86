namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Encapsulates the reconciliation decision for speculative nodes.
/// When a pre-existing speculative node at a given address is encountered during execution,
/// we compare its signature to the live memory signature:
/// <list type="bullet">
///   <item><description>Byte-for-byte match: the speculative node is promoted in place.</description></item>
///   <item><description>Same opcode/grammar, only a non-final field differs (e.g. a self-modified
///     immediate): the difference is reducible, so the speculative node is merged with the live
///     instruction via <see cref="SignatureReducer"/> and the surviving node is promoted. This keeps
///     the speculatively-explored subgraph hanging off the node instead of throwing it away.</description></item>
///   <item><description>Different opcode/grammar: a genuine divergence, so the speculative node is
///     swept and the address is poisoned to stop future speculation.</description></item>
/// </list>
/// </summary>
public class SpeculativeReconciler {
    private readonly SpeculativePromoter _promoter;
    private readonly SpeculativeReachabilityPruner _pruner;
    private readonly SignatureReducer _signatureReducer;
    private readonly CfgNodeIndex _nodeIndex;

    public SpeculativeReconciler(SpeculativePromoter promoter, SpeculativeReachabilityPruner pruner,
        SignatureReducer signatureReducer, CfgNodeIndex nodeIndex) {
        _promoter = promoter;
        _pruner = pruner;
        _signatureReducer = signatureReducer;
        _nodeIndex = nodeIndex;
    }

    /// <summary>
    /// Reconciles a pre-existing speculative node with the live memory state.
    /// Returns <c>true</c> if the node was promoted (caller should use it directly).
    /// Returns <c>false</c> if the node was swept (caller should use the live replacement node
    /// instead). On a genuine divergence the address is also poisoned to stop future speculation.
    /// </summary>
    /// <param name="speculativeNode">The speculative node currently in the graph at the address.</param>
    /// <param name="liveInstruction">The live (parsed/observed) instruction at the same address.</param>
    /// <param name="address">The segmented address being reconciled.</param>
    public bool Reconcile(CfgInstruction speculativeNode, CfgInstruction liveInstruction, SegmentedAddress address) {
        if (speculativeNode.Signature.ListEquivalent(liveInstruction.Signature.SignatureValue)) {
            _promoter.Promote(speculativeNode);
            return true;
        }
        // Same opcode/grammar, only a non-final field diverges (e.g. a self-modified immediate):
        // reducible. Merge instead of sweeping so the speculatively-explored subgraph survives.
        // The speculative node is passed first so the reducer keeps it as the survivor (it is the
        // first uncompiled instruction); its diverging field is nullified and it is promoted in place.
        // The live instruction is a throwaway parse not registered anywhere, so replacing it is a no-op
        // for every replacer subscriber.
        bool sameFinalSignature = speculativeNode.SignatureFinal.Equals(liveInstruction.SignatureFinal);
        if (sameFinalSignature) {
            CfgInstruction? reduced = _signatureReducer.ReduceToOne(speculativeNode, liveInstruction);
            if (reduced is not null) {
                _promoter.Promote(reduced);
                return true;
            }
        }
        // A genuine divergence: the speculative decode is wrong for the live bytes. Sweep the node
        // and its exclusively-reachable speculative subgraph. Reconcile runs at most once per node
        // (the graph-driven caller checks the index before delegating here; the parse-driven caller
        // passes a freshly-indexed node), so the sweep never runs twice on the same node.
        _pruner.Sweep(speculativeNode);
        // Poison only on a genuine divergence: a different opcode/grammar (final-field signatures
        // differ) is a real conflict. A reducible same-opcode variant is handled above, so reaching
        // here with matching final signatures means reduction was not possible; leave the address
        // un-poisoned so speculation can resume rather than killing the benefit there permanently.
        if (!sameFinalSignature) {
            _nodeIndex.PoisonSet.Add(address);
        }
        return false;
    }
}
