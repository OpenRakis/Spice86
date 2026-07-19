namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Persistent graph index grouping nodes by address, spanning all nodes (observed and speculative).
/// Unlike the hot memory caches <see cref="CurrentInstructions"/> / <see cref="PreviousInstructions"/>,
/// this index has no memory-state semantics and no eviction on write: it is a durable lookup table
/// for the CFG explorer and the cold-path promotion logic.
///
/// <para>Nodes are stored per address by identity (a node has a unique <see cref="CfgNode.Id"/>).
/// The index deliberately does NOT key on <see cref="Signature"/>: a signature's equality
/// (<see cref="Signature.ListEquivalent(System.Collections.Generic.IList{byte})"/>) treats null bytes
/// as wildcards and is therefore non-transitive, which makes it unsuitable as a hash/dictionary key;
/// and a node's computed signature changes when the signature reducer nullifies non-final fields,
/// which would leave a snapshot key stale. Signature-based queries instead scan the small per-address
/// list and read each node's live signature (see <see cref="GetAtAddressMatchingFinalSignature"/>).</para>
///
/// <para>Implements <see cref="InstructionReplacer"/> so that signature-reducer fan-out via
/// <see cref="InstructionReplacerRegistry"/> keeps the index coherent: when two instructions are
/// merged the old node is replaced by the surviving one.</para>
/// </summary>
public class CfgNodeIndex : InstructionReplacer {
    private readonly Dictionary<SegmentedAddress, List<CfgInstruction>> _index = new();

    /// <summary>
    /// Set of addresses proven byte-unstable between explore-time and execution-time.
    /// Speculation stops permanently at poisoned addresses.
    /// </summary>
    public HashSet<SegmentedAddress> PoisonSet { get; } = new();

    public CfgNodeIndex(InstructionReplacerRegistry replacerRegistry) : base(replacerRegistry) {
    }

    /// <summary>
    /// Inserts <paramref name="node"/> into the index.
    /// Idempotent by node identity: inserting a node already present at its address is a no-op.
    /// </summary>
    public void Insert(CfgInstruction node) {
        List<CfgInstruction> atAddress = DictionaryUtils.GetOrAddList(_index, node.Address);
        if (!atAddress.Contains(node)) {
            atAddress.Add(node);
        }
    }

    /// <summary>
    /// Removes <paramref name="node"/> from the index. Does not touch graph edges.
    /// </summary>
    public void Remove(CfgInstruction node) {
        DictionaryUtils.RemoveFromCollection(_index, node.Address, node);
    }

    /// <summary>
    /// Returns all nodes indexed at <paramref name="address"/>, or an empty enumerable if none.
    /// </summary>
    public IEnumerable<CfgInstruction> GetAtAddress(SegmentedAddress address) {
        if (!_index.TryGetValue(address, out List<CfgInstruction>? atAddress)) {
            return [];
        }
        return atAddress;
    }

    /// <summary>
    /// Returns whether the index has any entry at <paramref name="address"/>.
    /// </summary>
    public bool HasAddress(SegmentedAddress address) => _index.ContainsKey(address);

    /// <summary>
    /// Returns whether <paramref name="node"/> (by identity) is currently indexed at its address.
    /// A node that has been removed is no longer present.
    /// </summary>
    public bool Contains(CfgInstruction node) =>
        _index.TryGetValue(node.Address, out List<CfgInstruction>? atAddress) && atAddress.Contains(node);

    /// <summary>
    /// Returns the node at <paramref name="address"/> whose final-field signature matches
    /// <paramref name="signatureFinal"/>, or <c>null</c> when none matches. An observed node is
    /// preferred over a speculative one when both match.
    /// <para>Matching on <see cref="CfgInstruction.SignatureFinal"/> (grammar + opcode, never
    /// nullified by signature reduction) answers "is this the same instruction?": different opcodes
    /// do not match (so distinct self-modified variants stay separate), while instructions differing
    /// only in a non-final field (e.g. an immediate) do match and converge, leaving the value to the
    /// reduction machinery. The comparison reads each node's live <see cref="CfgInstruction.SignatureFinal"/>.</para>
    /// </summary>
    public CfgInstruction? GetAtAddressMatchingFinalSignature(SegmentedAddress address, Signature signatureFinal) {
        CfgInstruction? speculativeMatch = null;
        foreach (CfgInstruction node in GetAtAddress(address)) {
            if (!node.SignatureFinal.Equals(signatureFinal)) {
                continue;
            }
            if (!node.IsSpeculative) {
                return node;
            }
            speculativeMatch ??= node;
        }
        return speculativeMatch;
    }

    /// <summary>
    /// Handles signature-reducer fan-out: replaces <paramref name="oldInstruction"/> in the index
    /// with <paramref name="newInstruction"/>. Matching is by node identity, so it is unaffected by
    /// signature changes. No-op when <paramref name="oldInstruction"/> is not indexed.
    /// </summary>
    public override void ReplaceInstruction(CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        if (!DictionaryUtils.RemoveFromCollection(_index, oldInstruction.Address, oldInstruction)) {
            return;
        }
        List<CfgInstruction> atAddress = DictionaryUtils.GetOrAddList(_index, oldInstruction.Address);
        // Avoid duplicates: the new instruction may already be present at its address
        if (!atAddress.Contains(newInstruction)) {
            atAddress.Add(newInstruction);
        }
    }

    /// <summary>
    /// Handles removal fan-out: evicts <paramref name="instruction"/> from the index. Delegates to
    /// the existing <see cref="Remove"/> logic.
    /// </summary>
    public override void RemoveInstruction(CfgInstruction instruction) {
        Remove(instruction);
    }
}
