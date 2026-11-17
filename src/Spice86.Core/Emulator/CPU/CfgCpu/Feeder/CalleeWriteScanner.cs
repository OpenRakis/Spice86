namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Answers "which registers may this call change": scans the already-decoded static subgraph of
/// a callee entry (observed and speculative nodes alike) and unions the
/// <see cref="RegisterWriteSet"/> of every instruction on every path. Nested calls contribute the
/// callee's own mask when it resolves; an <c>int</c> contributes the writes of the installed
/// emulator handler's service contract when the vector still belongs to the emulator and AH,
/// AL or AX selects a known service. An indirect call inside the callee unions the scans of its observed
/// (non-speculative) targets; an indirect jump is followed into its observed targets, whose
/// returns count as the callee's own. Anything the scan cannot account for - an undecoded
/// address, an invalid node, a far jump, an indirect site with no observed or unscannable
/// target, a leaf with no static successor, an instruction whose
/// write set is not understood, a nested call to a callee still being scanned, or a subgraph over
/// <see cref="CalleeReturnScanner.MaxNodes"/> nodes - makes the whole result unknown
/// (<see cref="RegisterBits.AllExceptCs"/>).
///
/// <para>When the static result for a callee is unknown and <see cref="ObservedCallPreservation"/>
/// recorded at least one balanced return of it, the answer is every register except CS that was
/// not given back unchanged on all of those returns. A known static result always wins. A scan
/// that used such evidence is cached with <see cref="ObservedCallPreservation.Version"/> and
/// recomputed once a recorded mask has shrunk or an entry was forgotten since, so a stale mask
/// is never used. Discovery-only, like the rest of the scan: no generator code may use it.</para>
///
/// <para>A node from which none of the callee's returns can be reached (for example an error path
/// that jumps into a restart loop) contributes nothing, as long as the scan can follow every
/// transfer on the way: an undecoded successor, an indirect transfer without usable observed
/// targets, a far jump or a call whose callee is not known to return counts as reaching a return.</para>
///
/// <para>Returns add SP to the raw mask; <see cref="MayWrite"/> removes SP and the registers
/// the callee saves and restores from the answer only when
/// <paramref name="returnsBalanced"/> says the callee returns balanced and no return of its
/// own subgraph pops an immediate. The rule is applied at query time because the balance
/// evidence may change after a result was cached. A write to a saved stack slot through
/// memory (for example <c>mov [bp+2],ax</c>) is not detected: acceptable because the scan
/// is discovery-only and its results only propose guarded cases.</para>
///
/// <para>Complete results are cached per callee entry together with the addresses the scan
/// visited; <see cref="CfgNodeIndex.AddressChanged"/> drops every cached scan that visited the
/// changed address, covering sweeps, new nodes and self-modified variants with one rule. Results
/// that are unknown or went through an <c>int</c> (IVT- and AH-dependent) are never cached.</para>
/// </summary>
internal sealed class CalleeWriteScanner {
    /// <summary>One cached scan: the raw write mask (SP included), the registers the callee
    /// saves and restores, whether one of the callee's own returns pops an immediate, every
    /// address the scan visited (including nested callees), used for invalidation, and the
    /// <see cref="ObservedCallPreservation.Version"/> it was computed under when it used observed
    /// evidence (null when it did not).</summary>
    private sealed record CachedScan(ushort Mask, ushort Saved, bool PopsImmediate,
        HashSet<SegmentedAddress> Addresses, int? ObservedVersion);

    /// <summary>Per top-level scan context: the entries currently being scanned (a nested call
    /// back into one of them is a cycle and gives unknown) and whether such a cycle was hit, in
    /// which case the outer result is context-dependent and must not be cached.</summary>
    private sealed class ScanContext {
        public HashSet<SegmentedAddress> InProgress { get; } = [];
        public bool ContextDependent { get; set; }
    }

    private const ushort Unknown = (ushort)RegisterBits.AllExceptCs;

    private readonly CfgNodeIndex _nodeIndex;
    private readonly InstalledInterruptHandlers _installedHandlers;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly Func<SegmentedAddress, bool> _returnsBalanced;
    private readonly ObservedCallPreservation _observedPreservation;
    private readonly ILogger _logger;
    private readonly Dictionary<SegmentedAddress, CachedScan> _cache = new();
    private readonly Dictionary<SegmentedAddress, List<SegmentedAddress>> _cachedEntriesByAddress = new();

    public CalleeWriteScanner(CfgNodeIndex nodeIndex, InstalledInterruptHandlers installedHandlers,
        InterruptVectorTable interruptVectorTable, Func<SegmentedAddress, bool> returnsBalanced,
        ObservedCallPreservation observedPreservation, ILogger logger) {
        _nodeIndex = nodeIndex;
        _installedHandlers = installedHandlers;
        _interruptVectorTable = interruptVectorTable;
        _returnsBalanced = returnsBalanced;
        _observedPreservation = observedPreservation;
        _logger = logger;
        _nodeIndex.AddressChanged += ForgetScansThrough;
    }

    /// <summary>The <see cref="RegisterBits"/> mask of registers the callee at
    /// <paramref name="calleeEntry"/> may write, SP and registers the callee saves and restores
    /// removed when the callee is known to return balanced.
    /// <see cref="RegisterBits.AllExceptCs"/> when the subgraph is not fully understood.</summary>
    public ushort MayWrite(SegmentedAddress calleeEntry) => MayWrite(calleeEntry, new ScanContext()).Mask;

    /// <summary>What executing <paramref name="callNode"/> may change for the code after it:
    /// the callee's mask for a direct near or far call, the contract writes of the installed
    /// handler for an <c>int</c>, unknown for an indirect site.</summary>
    public ushort MayWriteAcross(CfgInstruction callNode) => MayWriteAcross(callNode, new ScanContext()).Mask;

    /// <summary>What a nested resolution reports to its caller: the mask, every address the
    /// nested scan depends on (the caller's own invalidation must cover them), whether the
    /// result may be cached (false when it went through an <c>int</c>), and whether it used
    /// observed evidence (then a cached caller must be checked against the evidence version).</summary>
    private sealed record NestedResult(ushort Mask, bool Cacheable, bool Observed,
        HashSet<SegmentedAddress> Addresses) {
        public static NestedResult UnknownResult { get; } = new(Unknown, false, false, []);
    }

    /// <summary>The static answer for <paramref name="calleeEntry"/>; when it is unknown and the
    /// callee was seen to return balanced, the registers not preserved on every observed return.</summary>
    private NestedResult MayWrite(SegmentedAddress calleeEntry, ScanContext context) {
        NestedResult result = StaticMayWrite(calleeEntry, context);
        if (result.Mask != Unknown
            || !_observedPreservation.TryGetPreserved(calleeEntry, out ushort preserved, out int count)) {
            return result;
        }
        ushort mask = (ushort)(Unknown & ~preserved);
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug(
                "Callee write scan: {Entry} has no static answer; {Count} observed balanced returns give writes {Mask:X4}",
                calleeEntry, count, mask);
        }
        return new NestedResult(mask, true, true, []);
    }

    private NestedResult StaticMayWrite(SegmentedAddress calleeEntry, ScanContext context) {
        if (_cache.TryGetValue(calleeEntry, out CachedScan? cached)) {
            if (cached.ObservedVersion is not { } version || version == _observedPreservation.Version) {
                return new NestedResult(ApplyBalance(calleeEntry, cached), true,
                    cached.ObservedVersion is not null, cached.Addresses);
            }
            RemoveCached(calleeEntry);
        }
        if (!context.InProgress.Add(calleeEntry)) {
            context.ContextDependent = true;
            return NestedResult.UnknownResult;
        }
        ScanOutcome outcome = Scan(calleeEntry, context);
        context.InProgress.Remove(calleeEntry);
        if (!outcome.Succeeded) {
            return NestedResult.UnknownResult;
        }
        int? observedVersion = outcome.Observed ? _observedPreservation.Version : null;
        cached = new CachedScan(outcome.Mask, outcome.Saved, outcome.PopsImmediate, outcome.Addresses,
            observedVersion);
        if (outcome.Cacheable && !context.ContextDependent) {
            Store(calleeEntry, cached);
        }
        return new NestedResult(ApplyBalance(calleeEntry, cached), outcome.Cacheable, outcome.Observed,
            outcome.Addresses);
    }

    private NestedResult MayWriteAcross(CfgInstruction callNode, ScanContext context) {
        if (InterruptCallFacts.TryGetInterruptVector(callNode, out byte vector)) {
            // An int resolution depends on the IVT and on AH: never cached.
            return new NestedResult(InterruptWrites(callNode, vector), false, false, []);
        }
        if (callNode.StaticSuccessorAddresses.Count == 1) {
            return MayWrite(callNode.StaticSuccessorAddresses[0], context);
        }
        return NestedResult.UnknownResult;
    }

    /// <summary>The raw mask and saved registers a scan computes: SP and Saved kept, since only
    /// the query side knows whether the callee's return is balanced.</summary>
    private sealed record ScanOutcome(bool Succeeded, bool Cacheable, bool Observed, ushort Mask, ushort Saved,
        bool PopsImmediate, HashSet<SegmentedAddress> Addresses) {
        public static ScanOutcome Unknown { get; } = new(false, false, false, 0, 0, false, []);
    }

    /// <summary>What the scan learned about one node of the callee's region: the registers it may
    /// write (unknown when the scan cannot tell), whether control may reach one of the callee's
    /// returns from it without going through another region node (a return, or anything the scan
    /// cannot follow), the region nodes it continues at, the across result of a call, and whether
    /// a return pops an immediate.</summary>
    private sealed record RegionNode(ushort Mask, bool MayReturn, IReadOnlyList<CfgInstruction> Successors,
        NestedResult? Across, bool PopsImmediate) {
        public static RegionNode Escape { get; } = new(Unknown, true, [], null, false);
    }

    /// <summary>Explores the callee's decoded region, then unions the writes of the nodes from
    /// which one of its returns may be reached. A node that cannot reach a return contributes
    /// nothing: control never comes back to the continuation through it. Anything the scan cannot
    /// follow (an undecoded successor, an indirect transfer without usable observed targets, a far
    /// jump, a call whose callee is not known to return) counts as reaching a return, so the
    /// rule only applies to regions the scan fully understands.</summary>
    private ScanOutcome Scan(SegmentedAddress calleeEntry, ScanContext context) {
        Queue<CfgInstruction> work = new(_nodeIndex.GetAtAddress(calleeEntry));
        if (work.Count == 0) {
            return ScanOutcome.Unknown;
        }
        Dictionary<CfgInstruction, RegionNode> region = [];
        HashSet<SegmentedAddress> addresses = [calleeEntry];
        while (work.Count > 0) {
            CfgInstruction node = work.Dequeue();
            if (region.ContainsKey(node)) {
                continue;
            }
            if (region.Count >= CalleeReturnScanner.MaxNodes) {
                return ScanOutcome.Unknown;
            }
            addresses.Add(node.Address);
            RegionNode facts = Describe(node, context);
            region[node] = facts;
            if (facts.Across is { } nested) {
                addresses.UnionWith(nested.Addresses);
            }
            foreach (CfgInstruction successor in facts.Successors) {
                work.Enqueue(successor);
            }
        }
        HashSet<CfgInstruction> reachingReturn = NodesReachingReturn(region);
        Dictionary<CfgInstruction, ushort> acrossMasks = [];
        ushort mask = 0;
        bool popsImmediate = false;
        bool cacheable = true;
        bool observed = false;
        foreach (CfgInstruction node in reachingReturn) {
            RegionNode facts = region[node];
            if (facts.Mask == Unknown) {
                return ScanOutcome.Unknown;
            }
            mask |= facts.Mask;
            popsImmediate |= facts.PopsImmediate;
            if (facts.Across is { } across) {
                acrossMasks[node] = across.Mask;
                cacheable &= across.Cacheable;
                observed |= across.Observed;
            }
        }
        ushort saved = CalleeSavedRegisters.Compute(calleeEntry, _nodeIndex, reachingReturn, acrossMasks);
        return new ScanOutcome(true, cacheable, observed, mask, saved, popsImmediate, addresses);
    }

    private RegionNode Describe(CfgInstruction node, ScanContext context) {
        if (node.IsInvalid || node.ExecutionAst is null) {
            return RegionNode.Escape;
        }
        IVisitableAstNode last = node.ExecutionAst is BlockNode block && block.Statements.Count > 0
            ? block.Statements[^1]
            : node.ExecutionAst;
        if (node.IsReturn) {
            if (last is not (ReturnNearNode or ReturnFarNode or ReturnInterruptNode)) {
                return RegionNode.Escape;
            }
            bool pops = last switch {
                ReturnNearNode ret => PopsImmediate(ret.BytesToPop),
                ReturnFarNode ret => PopsImmediate(ret.BytesToPop),
                _ => false
            };
            return new RegionNode((ushort)RegisterBits.Sp, true, [], null, pops);
        }
        if (node.IsCall) {
            return DescribeCall(node, context);
        }
        if (node.IsJump) {
            if (last is JumpFarNode) {
                return RegionNode.Escape;
            }
            // An indirect jump stays inside the callee: the observed targets' own returns count
            // as this callee's returns.
            List<CfgInstruction>? targets = node.StaticSuccessorAddresses.Count == 0
                ? ObservedTargetNodes(node)
                : NodesAt(node.StaticSuccessorAddresses);
            return targets is null ? RegionNode.Escape : new RegionNode(0, false, targets, null, false);
        }
        List<CfgInstruction>? successors = NodesAt(node.StaticSuccessorAddresses);
        if (node.StaticSuccessorAddresses.Count == 0 || successors is null) {
            return RegionNode.Escape;
        }
        return new RegionNode(RegisterWriteSet.MayWrite(node), false, successors, null, false);
    }

    /// <summary>A call continues at the instruction after it, which must be decoded. It reaches a
    /// return through that continuation only when its callee is known to return; otherwise it
    /// counts as reaching one itself.</summary>
    private RegionNode DescribeCall(CfgInstruction node, ScanContext context) {
        NestedResult across = AcrossInScan(node, context);
        List<CfgInstruction> continuation = _nodeIndex.GetAtAddress(node.NextInMemoryAddress32.ToSegmentedAddress()).ToList();
        if (continuation.Count == 0) {
            return new RegionNode(Unknown, true, [], across, false);
        }
        return new RegionNode(across.Mask, !CalleeReturns(node, across), continuation, across, false);
    }

    /// <summary>An <c>int</c> to an emulator handler the scan understands, a direct call to a callee
    /// known to return balanced, or an indirect call whose observed targets all are.</summary>
    private bool CalleeReturns(CfgInstruction callNode, NestedResult across) {
        if (InterruptCallFacts.TryGetInterruptVector(callNode, out _)) {
            return across.Mask != Unknown;
        }
        if (callNode.StaticSuccessorAddresses.Count == 1) {
            return _returnsBalanced(callNode.StaticSuccessorAddresses[0]);
        }
        HashSet<SegmentedAddress> targets = ObservedTargets(callNode);
        return targets.Count > 0 && targets.All(_returnsBalanced);
    }

    /// <summary>The region nodes from which a node that may reach a return is reachable (that
    /// node included), by a backward walk over the region's successor edges.</summary>
    private static HashSet<CfgInstruction> NodesReachingReturn(Dictionary<CfgInstruction, RegionNode> region) {
        Dictionary<CfgInstruction, List<CfgInstruction>> predecessors = [];
        HashSet<CfgInstruction> reaching = [];
        Queue<CfgInstruction> work = new();
        foreach ((CfgInstruction node, RegionNode facts) in region) {
            foreach (CfgInstruction successor in facts.Successors) {
                if (!predecessors.TryGetValue(successor, out List<CfgInstruction>? list)) {
                    list = predecessors[successor] = [];
                }
                list.Add(node);
            }
            if (facts.MayReturn && reaching.Add(node)) {
                work.Enqueue(node);
            }
        }
        while (work.Count > 0) {
            if (!predecessors.TryGetValue(work.Dequeue(), out List<CfgInstruction>? list)) {
                continue;
            }
            foreach (CfgInstruction predecessor in list) {
                if (reaching.Add(predecessor)) {
                    work.Enqueue(predecessor);
                }
            }
        }
        return reaching;
    }

    /// <summary>The distinct observed targets of an indirect site: its Normal-type successors
    /// that are neither speculative nodes nor speculatively wired edges. Speculative targets
    /// are never used.</summary>
    internal static HashSet<SegmentedAddress> ObservedTargets(CfgInstruction site) {
        HashSet<SegmentedAddress> targets = [];
        if (!site.SuccessorsPerType.TryGetValue(InstructionSuccessorType.Normal, out ISet<ICfgNode>? successors)) {
            return targets;
        }
        foreach (ICfgNode successor in successors) {
            if (!successor.IsSpeculative && !site.SpeculativelyWiredSuccessors.Contains(successor)) {
                targets.Add(successor.Address);
            }
        }
        return targets;
    }

    /// <summary>The across-mask of a call inside a scan: the public rule for a static site or
    /// an <c>int</c>, the union of the observed targets' masks for an indirect site (none,
    /// more than the per-site cap, or an unscannable target make it unknown).</summary>
    private NestedResult AcrossInScan(CfgInstruction callNode, ScanContext context) {
        if (callNode.StaticSuccessorAddresses.Count == 0
            && !InterruptCallFacts.TryGetInterruptVector(callNode, out _)) {
            return IndirectAcross(callNode, context);
        }
        return MayWriteAcross(callNode, context);
    }

    private NestedResult IndirectAcross(CfgInstruction callNode, ScanContext context) {
        HashSet<SegmentedAddress> targets = ObservedTargets(callNode);
        if (targets.Count == 0 || targets.Count > SpeculativeExplorer.MaxCandidatesPerSite) {
            return NestedResult.UnknownResult;
        }
        ushort mask = 0;
        bool cacheable = true;
        HashSet<SegmentedAddress> addresses = [];
        bool observed = false;
        foreach (SegmentedAddress target in targets) {
            NestedResult result = MayWrite(target, context);
            if (result.Mask == Unknown) {
                return NestedResult.UnknownResult;
            }
            mask |= result.Mask;
            cacheable &= result.Cacheable;
            observed |= result.Observed;
            addresses.UnionWith(result.Addresses);
        }
        return new NestedResult(mask, cacheable, observed, addresses);
    }

    /// <summary>The decoded nodes at the observed targets of an indirect site, or null when it has
    /// none, more than the per-site cap, or a target with no decoded node.</summary>
    private List<CfgInstruction>? ObservedTargetNodes(CfgInstruction node) {
        HashSet<SegmentedAddress> targets = ObservedTargets(node);
        if (targets.Count == 0 || targets.Count > SpeculativeExplorer.MaxCandidatesPerSite) {
            return null;
        }
        return NodesAt(targets);
    }

    /// <summary>The decoded nodes at every address, or null when one address has none.</summary>
    private List<CfgInstruction>? NodesAt(IEnumerable<SegmentedAddress> addresses) {
        List<CfgInstruction> nodes = [];
        foreach (SegmentedAddress address in addresses) {
            int before = nodes.Count;
            nodes.AddRange(_nodeIndex.GetAtAddress(address));
            if (nodes.Count == before) {
                return null;
            }
        }
        return nodes;
    }

    private static bool PopsImmediate(IVisitableAstNode bytesToPop) => bytesToPop switch {
        ConstantNode constant => constant.Value != 0,
        InstructionFieldNode field => field.ConstantValue != 0,
        _ => true
    };

    /// <summary>
    /// The writes the service behind <paramref name="vector"/> declares, when the IVT entry still
    /// belongs to the emulator and the service resolves to a known contract: a single-service
    /// handler, or a service selected by a constant AH, AL or AX loaded right before the <c>int</c>.
    /// Unknown otherwise (a program handler, an undescribed service, a selector the scan cannot
    /// read).
    /// </summary>
    private ushort InterruptWrites(CfgInstruction intNode, byte vector) {
        if (!_installedHandlers.TryGetInstalledAddress(vector, out SegmentedAddress installed)
            || installed != _interruptVectorTable[vector]
            || !_installedHandlers.Handlers.TryGetValue(vector, out IInterruptHandler? handler)) {
            return Unknown;
        }
        InterruptServiceEntry? entry = handler.Services switch {
            HandlerServices.SingleService single => single.Service,
            HandlerServices.SelectedServices selected => SelectedService(selected, intNode),
            _ => null
        };
        if (entry is null || ReferenceEquals(entry.Contract, ServiceContract.Unknown)) {
            return Unknown;
        }
        CpuValues writes = entry.Contract.Registers.Writes;
        foreach (InterruptServiceEntry variant in entry.Variants) {
            writes |= variant.Contract.Registers.Writes;
        }
        return ToRegisterBits(writes);
    }

    /// <summary>The service the selector register picks: AH from the instruction right before the
    /// <c>int</c>, AL or AX from constants the straight-line run before it loads. Null when the
    /// selector value is not a known constant or names no declared service.</summary>
    private static InterruptServiceEntry? SelectedService(HandlerServices.SelectedServices selected,
        CfgInstruction intNode) {
        int? selector = selected.Selector switch {
            ServiceSelector.Ah => InterruptCallFacts.AhLoadedBefore(intNode),
            ServiceSelector.Al => InterruptCallFacts.AxBytesLoadedBefore(intNode).Al,
            _ => AxLoadedBefore(intNode)
        };
        return selector is { } value ? selected.Services.GetValueOrDefault(value) : null;
    }

    private static int? AxLoadedBefore(CfgInstruction intNode) {
        (byte? al, byte? ah) = InterruptCallFacts.AxBytesLoadedBefore(intNode);
        if (al is null || ah is null) {
            return null;
        }
        return ah.Value << 8 | al.Value;
    }

    /// <summary>Converts contract <see cref="CpuValues"/> writes to <see cref="RegisterBits"/>:
    /// a 16-bit general register is written when either of its two low bytes is, a segment
    /// register i at CpuValues bit 32+i maps to bit 8+i. Flags are ignored.</summary>
    private static ushort ToRegisterBits(CpuValues writes) {
        ushort mask = 0;
        for (int register = 0; register < 8; register++) {
            if (((ulong)writes >> (4 * register) & 0x3UL) != 0) {
                mask |= (ushort)(1 << register);
            }
        }
        for (int segment = 0; segment < 6; segment++) {
            if (((ulong)writes >> (32 + segment) & 1UL) != 0) {
                mask |= (ushort)(1 << (8 + segment));
            }
        }
        return mask;
    }

    /// <summary>Applies the balance rule at query time: SP and the registers the callee saves
    /// and restores come out of the mask only when the callee is known to return balanced and
    /// none of its own returns pops an immediate.</summary>
    private ushort ApplyBalance(SegmentedAddress calleeEntry, CachedScan scan) =>
        _returnsBalanced(calleeEntry) && !scan.PopsImmediate
            ? (ushort)(scan.Mask & ~(ushort)RegisterBits.Sp & ~scan.Saved)
            : scan.Mask;

    private void Store(SegmentedAddress entry, CachedScan scan) {
        _cache[entry] = scan;
        foreach (SegmentedAddress address in scan.Addresses) {
            if (!_cachedEntriesByAddress.TryGetValue(address, out List<SegmentedAddress>? entries)) {
                entries = _cachedEntriesByAddress[address] = [];
            }
            entries.Add(entry);
        }
    }

    /// <summary>Drops every cached scan that visited <paramref name="address"/>: a node added,
    /// removed or replaced there may have changed the mask, and a newly observed target of an
    /// indirect site there may have changed the masks of the scans that crossed it.</summary>
    internal void ForgetScansThrough(SegmentedAddress address) {
        if (!_cachedEntriesByAddress.Remove(address, out List<SegmentedAddress>? affected)) {
            return;
        }
        foreach (SegmentedAddress entry in affected) {
            RemoveCached(entry);
        }
    }

    private void RemoveCached(SegmentedAddress entry) {
        if (!_cache.Remove(entry, out CachedScan? scan)) {
            return;
        }
        foreach (SegmentedAddress visitedAddress in scan.Addresses) {
            if (_cachedEntriesByAddress.TryGetValue(visitedAddress, out List<SegmentedAddress>? entries)
                && entries.Remove(entry) && entries.Count == 0) {
                _cachedEntriesByAddress.Remove(visitedAddress);
            }
        }
    }
}
