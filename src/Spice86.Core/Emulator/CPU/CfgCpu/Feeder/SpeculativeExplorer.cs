namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Performs static recursive-descent exploration of the CFG along statically-knowable successor
/// edges, starting from a freshly observed instruction. All nodes created here are marked
/// <see cref="CfgInstruction.SetSpeculative"/> = <c>true</c> and inserted into
/// <see cref="CfgNodeIndex"/>; they are never placed in <see cref="CurrentInstructions"/> or
/// <see cref="PreviousInstructions"/>.
///
/// <para>The explorer emits a fully-formed live graph via <see cref="NodeLinker.Link"/>:
/// edges and blocks are built at explore time using the same reconciliation path as observed edges.
/// On promotion the edges remain in place (in-place flip); on mismatch the pruner sweeps real edges.</para>
///
/// <para>The explorer stops at:</para>
/// <list type="bullet">
///   <item><description>Addresses already holding a node with the same final-field signature (observed or
///     previously speculated): a convergence edge is wired from the predecessor to that existing node.
///     A node with a different opcode at the same address (self-modified variant) does not converge;
///     a distinct variant is minted instead.</description></item>
///   <item><description>Addresses in the <see cref="CfgNodeIndex.PoisonSet"/>.</description></item>
///   <item><description>A zero instruction (<c>00 00</c>) whose two following bytes are also zero: the
///     run is walking a zero-filled buffer. Nothing is remembered, so memory a program later fills
///     with code is decoded normally. A lone zero instruction followed by code is still decoded.</description></item>
///   <item><description>Instructions with no static successors (indirect jumps, RET, IRET, invalid).</description></item>
///   <item><description>CALL successors: the callee entry is enqueued but call-continuations are not
///     (unless under known-safe trust).</description></item>
/// </list>
///
/// <para><b>Known-safe seeding</b> (<see cref="SeedKnownSafe"/>): seeds emulator-installed interrupt
/// handler entry points with continuation-following enabled. Under trust, call/int instructions get
/// an eagerly-wired <see cref="InstructionSuccessorType.CallToReturn"/> continuation edge to their
/// <see cref="CfgInstruction.NextInMemoryAddress32"/> (computed from instruction length, never by
/// decoding through a possibly-switchable operand). Trust propagates along intra-routine successors
/// and also crosses into callees, so nested calls reached from a known-safe entry point have their
/// own continuations explored as well.</para>
/// </summary>
public class SpeculativeExplorer {
    /// <summary>
    /// Per-worklist-item data carrying the target address, predecessor, edge type, trust flag and
    /// discovery source. Trust (<see cref="FollowContinuations"/>) propagates along intra-routine
    /// successors (Normal edges) and also into callees. Only trusted items eagerly wire
    /// <see cref="InstructionSuccessorType.CallToReturn"/> continuation edges past call/int
    /// instructions. <see cref="Trigger"/> is the instruction a <see cref="DiscoveryCandidate"/>
    /// was submitted under; a data-sourced item whose bytes are not trusted is kept pending on it.
    /// </summary>
    private readonly record struct ExploreItem(
        SegmentedAddress Target,
        CfgInstruction? Predecessor,
        InstructionSuccessorType EdgeType,
        bool FollowContinuations,
        ExploreSource Source,
        CfgInstruction? Trigger);

    private const int MaxCandidatesPerSite = 32;

    private readonly IMemory _memory;
    private readonly InstructionParser _instructionParser;
    private readonly CfgNodeIndex _nodeIndex;
    private readonly NodeLinker _nodeLinker;
    private readonly ChangedMemoryMap? _changedMemoryMap;
    private readonly ILogger? _loggerService;
    private readonly Dictionary<CfgInstruction, List<DiscoveryCandidate>> _pending = new();
    private readonly Dictionary<CfgInstruction, HashSet<SegmentedAddress>> _siteTargets = new();
    private readonly HashSet<CfgInstruction> _cappedSites = [];

    public SpeculativeExplorer(IMemory memory, InstructionParser instructionParser, CfgNodeIndex nodeIndex, NodeLinker nodeLinker,
        ChangedMemoryMap? changedMemoryMap, ILogger? loggerService) {
        _memory = memory;
        _instructionParser = instructionParser;
        _nodeIndex = nodeIndex;
        _nodeLinker = nodeLinker;
        _changedMemoryMap = changedMemoryMap;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Enqueues all statically-reachable successors of <paramref name="observed"/> that are not
    /// yet in the index or poison set, then drains the worklist by speculatively decoding each
    /// address in turn. Wires full successor/predecessor edges with block reconciliation between
    /// each predecessor and its explored target via <see cref="NodeLinker.Link"/>.
    /// All items are untrusted (no continuation-following).
    /// </summary>
    public void ExploreFrom(CfgInstruction observed) {
        Queue<ExploreItem> worklist = new();
        EnqueueStaticSuccessors(observed, worklist, followContinuations: false);
        Drain(worklist);
    }

    /// <summary>
    /// Seeds a known-safe interrupt handler entry point for speculative exploration with
    /// continuation-following enabled. Under trust, the explorer eagerly wires
    /// <see cref="InstructionSuccessorType.CallToReturn"/> edges past call/int instructions,
    /// allowing the full handler body to be decoded including post-call continuations.
    /// No-op if the address is already in the index (handler already explored or observed).
    /// </summary>
    /// <param name="entry">Handler entry address to seed.</param>
    public void SeedKnownSafe(SegmentedAddress entry) {
        if (_nodeIndex.PoisonSet.Contains(entry)) {
            return;
        }
        if (_nodeIndex.HasAddress(entry)) {
            return;
        }
        Queue<ExploreItem> worklist = new();
        worklist.Enqueue(new ExploreItem(entry, null, InstructionSuccessorType.Normal, FollowContinuations: true, ExploreSource.Static, null));
        Drain(worklist);
    }

    /// <summary>
    /// Submits discovery targets produced by a trigger firing (a table read, a register value, an
    /// installed vector, ...). Refused values (data-sourced offset or segment of 0x0000/0xFFFF) are
    /// dropped, and each site accepts at most 32 distinct non-static targets. Data-sourced
    /// candidates whose target bytes no longer hold their loaded values are kept pending on
    /// <paramref name="trigger"/> (dropped when it is null); everything else goes through the same
    /// decode-and-link pass as static exploration.
    /// </summary>
    public void ExploreCandidates(CfgInstruction? trigger, IReadOnlyList<DiscoveryCandidate> candidates) {
        Queue<ExploreItem> worklist = new();
        foreach (DiscoveryCandidate candidate in candidates) {
            TryEnqueueCandidate(trigger, candidate, worklist);
        }
        Drain(worklist);
    }

    /// <summary>
    /// Re-runs the candidates kept pending on <paramref name="trigger"/> because their target bytes
    /// were not trusted. Still-untrusted candidates stay pending; decoded, converged, invalid or
    /// dead-predecessor ones leave the set. One dictionary lookup when nothing is pending.
    /// </summary>
    public void RetryPending(CfgInstruction trigger) {
        if (!_pending.TryGetValue(trigger, out List<DiscoveryCandidate>? pending)) {
            return;
        }
        List<DiscoveryCandidate> retry = new(pending);
        pending.Clear();
        Queue<ExploreItem> worklist = new();
        foreach (DiscoveryCandidate candidate in retry) {
            if (candidate.Predecessor is { IsLive: false }) {
                continue;
            }
            worklist.Enqueue(new ExploreItem(candidate.Target, candidate.Predecessor,
                candidate.EdgeType, FollowContinuations: false, candidate.Source, trigger));
        }
        Drain(worklist);
        if (pending.Count == 0) {
            _pending.Remove(trigger);
        }
    }

    private void TryEnqueueCandidate(CfgInstruction? trigger, DiscoveryCandidate candidate, Queue<ExploreItem> worklist) {
        if (IsDataSource(candidate.Source) && IsRefused(candidate.Target)) {
            return;
        }
        if (candidate.Source != ExploreSource.Static) {
            CfgInstruction? site = candidate.Predecessor ?? trigger;
            if (site is not null && !AcceptForSite(site, candidate.Target)) {
                return;
            }
        }
        worklist.Enqueue(new ExploreItem(candidate.Target, candidate.Predecessor,
            candidate.EdgeType, FollowContinuations: false, candidate.Source, trigger));
    }

    private bool AcceptForSite(CfgInstruction site, SegmentedAddress target) {
        HashSet<SegmentedAddress> targets = _siteTargets.TryGetValue(site, out HashSet<SegmentedAddress>? existing)
            ? existing
            : _siteTargets[site] = [];
        if (!targets.Add(target)) {
            return true;
        }
        if (targets.Count <= MaxCandidatesPerSite) {
            return true;
        }
        if (_cappedSites.Add(site)) {
            _loggerService?.LogDebug(
                "Discovery cap reached at {Address}: over {Max} distinct non-static targets for one site",
                site.Address, MaxCandidatesPerSite);
        }
        return false;
    }

    private void AddPending(CfgInstruction trigger, DiscoveryCandidate candidate) {
        if (!_pending.TryGetValue(trigger, out List<DiscoveryCandidate>? pending)) {
            pending = _pending[trigger] = [];
        }
        if (!pending.Contains(candidate)) {
            pending.Add(candidate);
        }
    }

    private static bool IsDataSource(ExploreSource source) => source is
        ExploreSource.TableEntry or ExploreSource.CatalogueEntry or ExploreSource.InterruptVector
        or ExploreSource.RegisterValue or ExploreSource.PointerValue;

    private static bool IsRefused(SegmentedAddress target) =>
        target.Offset is 0x0000 or 0xFFFF || target.Segment is 0x0000 or 0xFFFF;

    /// <summary>
    /// Every byte of the parsed instruction still holds the value its loader wrote. The physical
    /// address is computed like a code fetch (MMU translation then A20 gate) so the check lines up
    /// with the bytes the CPU would execute.
    /// </summary>
    private bool BytesHoldLoadedValue(CfgInstruction candidate) {
        if (_changedMemoryMap is null) {
            return false;
        }
        for (int i = 0; i < candidate.Length; i++) {
            uint physical = _memory.A20Gate.TransformAddress(
                _memory.Mmu.TranslateAddress(candidate.Address.Segment, (uint)candidate.Address.Offset + (uint)i));
            if (!_changedMemoryMap.HoldsLoadedValue(physical)) {
                return false;
            }
        }
        return true;
    }

    // Iteratively decodes and links all reachable instructions from the worklist.
    private void Drain(Queue<ExploreItem> worklist) {
        while (worklist.Count > 0) {
            ExploreItem item = worklist.Dequeue();
            SegmentedAddress address = item.Target;
            if (_nodeIndex.PoisonSet.Contains(address)) {
                continue;
            }
            CfgInstruction candidate = _instructionParser.ParseInstructionAt(address);
            if (candidate.IsInvalid) {
                // Only static-edge exploration poisons; a guess from any other source is dropped
                // so the address can be discovered again once its bytes mean something.
                if (item.Source == ExploreSource.Static) {
                    _nodeIndex.PoisonSet.Add(address);
                }
                continue;
            }
            // Converge only onto a node that is the SAME instruction (same final-field signature)
            // as the bytes currently in memory. Matching by address alone would wire a predecessor
            // to an unrelated self-modified variant living at the same address.
            // This links directly to that node rather than through a SelectorNode; that is safe
            // because a node reached this way is always non-live (speculative) or re-checked against
            // memory before execution (an observed node goes stale and gets reconciled the moment its
            // bytes actually change - see CfgNodeFeeder.DetermineToExecute), so a wrong guess here can
            // never be executed silently. SelectorNodes are only ever created between two observed
            // (non-speculative) variants once a real conflict is proven; see NodeLinker.CreateSelectorNodeBetween.
            CfgInstruction? existing = _nodeIndex.GetAtAddressMatchingFinalSignature(address, candidate.SignatureFinal);
            if (existing is not null) {
                if (item.Predecessor is not null) {
                    _nodeLinker.Link(item.EdgeType, item.Predecessor, existing);
                }
                continue;
            }
            if (StartsZeroChain(candidate)) {
                continue;
            }
            if (IsDataSource(item.Source) && !BytesHoldLoadedValue(candidate)) {
                // The target bytes may be garbage the program has not loaded or decompressed yet;
                // keep the candidate pending on its trigger instead of decoding or poisoning.
                if (item.Trigger is not null && item.Predecessor is not { IsLive: false }) {
                    AddPending(item.Trigger, new DiscoveryCandidate(item.Target, item.Predecessor, item.EdgeType, item.Source));
                }
                continue;
            }
            candidate.SetSpeculative(true);
            candidate.SetDiscoverySource(item.Source);
            _nodeIndex.Insert(candidate);
            if (item.Predecessor is not null) {
                _nodeLinker.Link(item.EdgeType, item.Predecessor, candidate);
            }
            EnqueueSuccessorsForNode(candidate, worklist, item.FollowContinuations);
        }
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is a <c>00 00</c> instruction whose two bytes in memory
    /// after it are also zero: the start of a zero-filled region rather than real code.
    /// </summary>
    private bool StartsZeroChain(CfgInstruction candidate) {
        if (candidate.Length != 2
            || candidate.Signature.SignatureValue[0] != 0
            || candidate.Signature.SignatureValue[1] != 0) {
            return false;
        }
        SegmentedAddress32 next = candidate.NextInMemoryAddress32;
        return SneakilyReadCodeByte(next.Segment, next.Offset) == 0
            && SneakilyReadCodeByte(next.Segment, next.Offset + 1) == 0;
    }

    /// <summary>Reads one byte at the physical address the CPU would fetch it from: the offset is
    /// translated like a code access (the 8086 MMU wraps it to 16 bits), the A20 gate is applied,
    /// and no memory read breakpoint fires.</summary>
    private byte SneakilyReadCodeByte(ushort segment, uint offset) {
        uint physical = _memory.Mmu.TranslateAddress(segment, offset);
        return _memory.SneakilyRead(_memory.A20Gate.TransformAddress(physical));
    }

    /// <summary>
    /// Classifies and enqueues successors of <paramref name="node"/> according to its instruction kind
    /// and the current trust level. Under trust, call/int instructions additionally get their
    /// continuation address enqueued as a <see cref="InstructionSuccessorType.CallToReturn"/> item.
    /// For callback instructions (Kind=None, MaxSuccessorsCount=null) under trust, the continuation
    /// is enqueued as a Normal successor since the parser does not register static successors for them.
    /// </summary>
    private void EnqueueSuccessorsForNode(CfgInstruction node, Queue<ExploreItem> worklist, bool followContinuations) {
        if (node.IsCall) {
            // Call or INT: static successors are callees (enqueued untrusted, always Normal edges).
            // Continuation is only enqueued if under trust.
            foreach (SegmentedAddress successor in node.StaticSuccessorAddresses.Where(address => !_nodeIndex.PoisonSet.Contains(address))) {
                worklist.Enqueue(new ExploreItem(successor, node, InstructionSuccessorType.Normal, followContinuations, ExploreSource.Static, null));
            }
            if (followContinuations) {
                EnqueueContinuation(node, worklist, InstructionSuccessorType.CallToReturn, followContinuations);
            }
            return;
        }
        if (node.StaticSuccessorAddresses.Count > 0) {
            // Non-call instructions with known static successors: propagate trust through intra-routine successors.
            // Static successors are always Normal edges.
            EnqueueStaticSuccessors(node, worklist, followContinuations);
            return;
        }
        // No static successors and not a call: either a callback instruction, a RET/IRET, or an indirect jump.
        // Only callback instructions have a meaningful memory continuation; follow it under trust.
        if (followContinuations && node.MaxSuccessorsCount is null && !node.IsReturn && !node.IsJump && !node.IsInvalid) {
            EnqueueContinuation(node, worklist, InstructionSuccessorType.Normal, followContinuations);
        }
    }

    /// <summary>
    /// Enqueues the call/int continuation address (NextInMemoryAddress32) as a CallToReturn item.
    /// The continuation address is computed from instruction length, never by decoding the operand.
    /// </summary>
    private void EnqueueContinuation(CfgInstruction callNode, Queue<ExploreItem> worklist, InstructionSuccessorType edgeType, bool followContinuations) {
        SegmentedAddress continuation = callNode.NextInMemoryAddress32.ToSegmentedAddress();
        if (_nodeIndex.PoisonSet.Contains(continuation)) {
            return;
        }
        worklist.Enqueue(new ExploreItem(continuation, callNode, edgeType, followContinuations, ExploreSource.Static, null));
    }

    private void EnqueueStaticSuccessors(CfgInstruction instruction, Queue<ExploreItem> worklist, bool followContinuations) {
        foreach (SegmentedAddress successor in instruction.StaticSuccessorAddresses.Where(address => !_nodeIndex.PoisonSet.Contains(address))) {
            worklist.Enqueue(new ExploreItem(successor, instruction, InstructionSuccessorType.Normal, followContinuations, ExploreSource.Static, null));
        }
    }
}
