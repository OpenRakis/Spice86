namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
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
public class SpeculativeExplorer : InstructionReplacer {
    /// <summary>
    /// Per-worklist-item data carrying the target address, predecessor, edge type, trust flag and
    /// discovery source. Trust (<see cref="FollowContinuations"/>) propagates along intra-routine
    /// successors (Normal edges) and also into callees. Only trusted items eagerly wire
    /// <see cref="InstructionSuccessorType.CallToReturn"/> continuation edges past call/int
    /// instructions. <see cref="Trigger"/> is the instruction a <see cref="DiscoveryCandidate"/>
    /// was submitted under; a data-sourced item whose bytes are not trusted is kept pending on it.
    /// <see cref="GuessRoot"/> is the guessed root node the item descends from, or null when the
    /// item is not under a guess: a guess-source item with no root is itself a root, and the node
    /// it inserts becomes the <see cref="GuessRoot"/> of every item below it. Items under a guess
    /// never poison.
    /// </summary>
    private readonly record struct ExploreItem(
        SegmentedAddress Target,
        CfgInstruction? Predecessor,
        InstructionSuccessorType EdgeType,
        bool FollowContinuations,
        ExploreSource Source,
        CfgInstruction? Trigger,
        ushort KnownRegisters,
        CfgInstruction? Anchor,
        CfgInstruction? GuessRoot);

    private const int MaxCandidatesPerSite = 32;
    private const int MaxTableEntries = 1024;

    private readonly IMemory _memory;
    private readonly State _state;
    private readonly SwitchTableRecognizer _tableRecognizer = new();
    private readonly InstructionParser _instructionParser;
    private readonly CfgNodeIndex _nodeIndex;
    private readonly NodeLinker _nodeLinker;
    private readonly ChangedMemoryMap _changedMemoryMap;
    private readonly ILogger _loggerService;
    private readonly InstalledInterruptHandlers _installedInterruptHandlers;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly FunctionCatalogue _functionCatalogue;
    private readonly CalleeReturnEvidence _calleeReturnEvidence = new();
    private readonly Dictionary<CfgInstruction, List<DiscoveryCandidate>> _pending = new();
    private readonly Dictionary<CfgInstruction, HashSet<SegmentedAddress>> _siteTargets = new();
    private readonly WatchedPointerCells _watchedCells;
    private readonly Dictionary<CfgInstruction, List<AnchorDependent>> _anchorDependents = new();
    private readonly HashSet<CfgInstruction> _pendingAnchorWalks = [];
    private readonly HashSet<(CfgInstruction Site, CfgInstruction Anchor)> _anchorSitePairs = new();
    private readonly Dictionary<CfgInstruction, ushort> _anchorKnownUnion = new();
    private readonly HashSet<(uint Start, int Size, CfgInstruction Site)> _watchedSiteCells = new();
    private int _refusedAnchorSiteForms;

    /// <summary>One evaluation a site registered on its anchor asks to run at each anchor
    /// execution.</summary>
    private abstract record AnchorDependent(CfgInstruction Site, bool IsCall);

    /// <summary>Near <c>jmp/call reg</c>: reads <see cref="Register"/> and proposes CS:that.</summary>
    private sealed record RegisterDependent(CfgInstruction Site, bool IsCall, int Register)
        : AnchorDependent(Site, IsCall);

    /// <summary>Reads the <see cref="CellSize"/>-byte pointer cell the operand formula resolves to
    /// (<see cref="IndexRegister"/> null for a plain <c>[seg:const]</c>) and proposes what it
    /// holds.</summary>
    private sealed record CellDependent(CfgInstruction Site, bool IsCall, int CellSize,
        int SegmentRegisterIndex, int? IndexRegister, ushort Displacement) : AnchorDependent(Site, IsCall);

    /// <summary>Reads every word entry of a bounded table under the anchor's current segment
    /// value and proposes each.</summary>
    private sealed record TableDependent(CfgInstruction Site, bool IsCall,
        int SegmentRegisterIndex, ushort BaseOffset, ushort Step, ushort Bound)
        : AnchorDependent(Site, IsCall);

    /// <summary>Reads the handler field of every record the site's backward walk proved the index
    /// register can point at, and proposes each as a guarded target.</summary>
    private sealed record RecordArrayDependent(CfgInstruction Site, bool IsCall,
        RecordArrayRecognizer.RecordArrayShape Shape) : AnchorDependent(Site, IsCall);
    /// <summary>One call site pending on its callee's return proof, with the (known-registers,
    /// anchor) pairs that reached it: when the callee proves, each pair resumes its walk past the
    /// call with the registers the callee does not write.</summary>
    private sealed record PendingCallSite(CfgInstruction Call,
        List<(ushort KnownBeforeCall, CfgInstruction Anchor)> Anchors);

    private readonly Dictionary<SegmentedAddress, List<PendingCallSite>> _pendingCallSites = new();
    private readonly HashSet<CfgInstruction> _cappedSites = [];
    private readonly CalleeReturnScanner _calleeReturnScanner;
    private readonly Dictionary<SegmentedAddress, CallDistance> _provedReturnKind = new();
    private readonly HashSet<SegmentedAddress> _refusedCalleeProof = [];
    private readonly HashSet<SegmentedAddress> _incompleteCalleeProof = [];
    private readonly CalleeWriteScanner _calleeWriteScanner;
    private readonly RecordArrayRecognizer _recordArrayRecognizer;
    private int _drainNesting;
    private bool _drainInserted;
    private readonly SpeculativeReachabilityPruner _pruner;
    private readonly Dictionary<CfgInstruction, CfgInstruction?> _rootGuessSite = new();
    private readonly List<(CfgInstruction Root, SegmentedAddress InvalidAddress)> _wrongRoots = new();
    private readonly HashSet<(SegmentedAddress Target, Signature Signature)> _rejectedGuesses = [];

    public SpeculativeExplorer(InstructionReplacerRegistry replacerRegistry, IMemory memory, InstructionParser instructionParser, CfgNodeIndex nodeIndex, NodeLinker nodeLinker,
        ChangedMemoryMap changedMemoryMap, InstalledInterruptHandlers installedInterruptHandlers,
        InterruptVectorTable interruptVectorTable, FunctionCatalogue functionCatalogue,
        WatchedPointerCells watchedCells, State state, ILogger loggerService) : base(replacerRegistry) {
        _memory = memory;
        _state = state;
        _instructionParser = instructionParser;
        _nodeIndex = nodeIndex;
        _nodeLinker = nodeLinker;
        _changedMemoryMap = changedMemoryMap;
        _installedInterruptHandlers = installedInterruptHandlers;
        _interruptVectorTable = interruptVectorTable;
        _functionCatalogue = functionCatalogue;
        _watchedCells = watchedCells;
        _loggerService = loggerService;
        _calleeReturnScanner = new CalleeReturnScanner(nodeIndex);
        _calleeWriteScanner = new CalleeWriteScanner(nodeIndex, installedInterruptHandlers,
            interruptVectorTable, ReturnsBalanced);
        _recordArrayRecognizer = new RecordArrayRecognizer(nodeIndex, _calleeWriteScanner.MayWriteAcross);
        _pruner = new SpeculativeReachabilityPruner(replacerRegistry);
        _nodeLinker.CallReturnLinked += OnCallReturnLinked;
        // Catalogue entries are trust-checked against the loaded bytes, so they can only be
        // submitted after the image capture ran. ImageCaptured fires at the end of that capture,
        // which makes the ordering explicit instead of relying on event subscription order.
        _changedMemoryMap.ImageCaptured += OnImageCaptured;
    }

    /// <inheritdoc />
    public override void ReplaceInstruction(CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        // Explorer state is keyed on the replaced node; nothing migrates to the new node today.
    }

    /// <inheritdoc />
    public override void RemoveInstruction(CfgInstruction instruction) {
        _anchorDependents.Remove(instruction);
        foreach ((CfgInstruction anchor, List<AnchorDependent> dependents) in _anchorDependents.ToList()) {
            dependents.RemoveAll(dependent => ReferenceEquals(dependent.Site, instruction));
            if (dependents.Count == 0) {
                _anchorDependents.Remove(anchor);
            }
        }
        _anchorSitePairs.RemoveWhere(pair => ReferenceEquals(pair.Site, instruction) || ReferenceEquals(pair.Anchor, instruction));
        _anchorKnownUnion.Remove(instruction);
        _pendingAnchorWalks.Remove(instruction);
        _siteTargets.Remove(instruction);
        _cappedSites.Remove(instruction);
        _watchedSiteCells.RemoveWhere(cell => ReferenceEquals(cell.Site, instruction));
        _pending.Remove(instruction);
        foreach ((CfgInstruction trigger, List<DiscoveryCandidate> pending) in _pending.ToList()) {
            pending.RemoveAll(candidate => ReferenceEquals(candidate.Predecessor, instruction));
            if (pending.Count == 0) {
                _pending.Remove(trigger);
            }
        }
        foreach ((SegmentedAddress callee, List<PendingCallSite> sites) in _pendingCallSites.ToList()) {
            sites.RemoveAll(site => {
                site.Anchors.RemoveAll(pair => ReferenceEquals(pair.Anchor, instruction));
                return ReferenceEquals(site.Call, instruction);
            });
            if (sites.Count == 0) {
                _pendingCallSites.Remove(callee);
            }
        }
        _rootGuessSite.Remove(instruction);
        foreach ((CfgInstruction root, CfgInstruction? site) in _rootGuessSite.ToList()) {
            if (ReferenceEquals(site, instruction)) {
                _rootGuessSite.Remove(root);
            }
        }
        // The registry calls the index before the explorer, so the address test runs after the
        // node's own removal: an address with no node left loses its callee proof state.
        if (!_nodeIndex.HasAddress(instruction.Address)) {
            _provedReturnKind.Remove(instruction.Address);
            _refusedCalleeProof.Remove(instruction.Address);
            _incompleteCalleeProof.Remove(instruction.Address);
        }
    }

    /// <summary>Raised once a predecessorless seed node has been decoded and given a block.
    /// The execution context registers such nodes as generation entry points; a seed no observed
    /// edge ever reaches would otherwise stay invisible to the exported block graph.</summary>
    internal event Action<CfgInstruction>? SpeculativeSeedCreated;

    /// <summary>
    /// Seeds one predecessorless candidate per catalogue entry that lands in the loaded image and
    /// is not yet in the graph, in a single batch. The physical address is computed like a code
    /// fetch (MMU translation then A20 gate) so an entry matches only when the CPU could execute
    /// it inside the image.
    /// </summary>
    private void OnImageCaptured(object? sender, LoadedImage image) {
        uint imageEnd = image.StartAddress + image.SizeInBytes;
        List<DiscoveryCandidate> candidates = [];
        foreach (FunctionInformation information in _functionCatalogue.FunctionInformations.Values) {
            uint physical = _memory.A20Gate.TransformAddress(
                _memory.Mmu.TranslateAddress(information.Address.Segment, information.Address.Offset));
            if (physical < image.StartAddress || physical >= imageEnd) {
                continue;
            }
            if (_nodeIndex.HasAddress(information.Address)) {
                continue;
            }
            if (_installedInterruptHandlers.IsInstalledHandler(information.Address)) {
                // Emulator-installed handlers are seeded and entry-registered through their own
                // channel; seeding them here would bypass that policy.
                continue;
            }
            candidates.Add(new DiscoveryCandidate(information.Address, null,
                InstructionSuccessorType.Normal, ExploreSource.CatalogueEntry));
        }
        if (candidates.Count > 0) {
            ExploreCandidates(null, candidates);
        }
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
        // The successors of the just-observed node inherit the register values the CPU holds right
        // before it executes: every register it does not write stays known down the speculative
        // path it opens, and it becomes the anchor that re-evaluates them on later executions.
        ushort known = (ushort)((ushort)RegisterBits.All & ~RegisterWriteSet.MayWrite(observed));
        EnqueueStaticSuccessors(observed, worklist, followContinuations: false, known, known == 0 ? null : observed, guessRoot: null);
        if (observed.IsCall) {
            // The walk starts from the CPU state right before the observed call runs, so the
            // continuation keeps every register the callee does not write.
            TryExploreCallContinuation(observed, worklist, (ushort)RegisterBits.All, observed, null);
        }
        TryExplorePushedReturn(observed, worklist);
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
        worklist.Enqueue(new ExploreItem(entry, null, InstructionSuccessorType.Normal, FollowContinuations: true, ExploreSource.Static, null, 0, null, GuessRoot: null));
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
    /// Called on every execution of an observed near indirect jmp/call through a word memory
    /// operand. Retries the data-sourced candidates pending on it, and when the execution linked a
    /// target edge never seen before, resolves the switch/call table the transfer may read: reads
    /// the N+1 entries at the pointer's segment and displacement, refuses the table when an entry
    /// is a refused value or an observed target is missing from it, else submits every entry as a
    /// <see cref="ExploreSource.TableEntry"/> candidate. For an indirect call the
    /// call-continuation rule is then applied with each entry as an explicit callee.
    /// </summary>
    public void ExploreIndirectTransfer(CfgInstruction transfer, bool newTargetEdge) {
        if (!newTargetEdge) {
            return;
        }
        SwitchTableRecognizer.TableShape? table = _tableRecognizer.Recognize(transfer);
        if (table is null) {
            // Not a bounded table: maybe an index into an array of records a search loop walked.
            if (_recordArrayRecognizer.Recognize(transfer) is { } shape) {
                EvaluateRecordArray(transfer, shape, transfer);
            }
            return;
        }
        int count = table.Bound + 1;
        if (count > MaxTableEntries) {
            _loggerService.LogDebug("Switch table at {Address} refused: {Count} entries over the {Max} maximum",
                transfer.Address, count, MaxTableEntries);
            return;
        }
        ushort segment = _state.SegmentRegisters.UInt16[(uint)table.SegmentRegisterIndex];
        HashSet<ushort> entries = new(count);
        for (int i = 0; i < count; i++) {
            ushort offset = (ushort)(table.BaseOffset + table.Step * i);
            ushort entry = (ushort)(SneakilyReadCodeByte(segment, offset)
                | SneakilyReadCodeByte(segment, (uint)offset + 1) << 8);
            if (entry is 0x0000 or 0xFFFF) {
                _loggerService.LogDebug("Switch table at {Address} refused: entry {Index} is 0x{Entry:X4}",
                    transfer.Address, i, entry);
                return;
            }
            entries.Add(entry);
        }
        // Every observed jump/call target of the transfer must be one of the entries, else this is
        // not a plain table. Only Normal edges carry table targets: CallToReturn and misaligned or
        // redirected continuation edges name the code after the call, never a table entry.
        if (!transfer.SuccessorsPerType.TryGetValue(InstructionSuccessorType.Normal, out ISet<ICfgNode>? targetSuccessors)) {
            targetSuccessors = new HashSet<ICfgNode>();
        }
        foreach (ICfgNode successor in targetSuccessors) {
            if (!successor.IsSpeculative
                && !(successor.Address.Segment == transfer.Address.Segment && entries.Contains(successor.Address.Offset))) {
                _loggerService.LogDebug("Switch table at {Address} refused: observed target {Target} is not an entry",
                    transfer.Address, successor.Address);
                return;
            }
        }
        List<DiscoveryCandidate> candidates = entries
            .Select(entry => new SegmentedAddress(transfer.Address.Segment, entry))
            .Where(address => !transfer.SuccessorsPerAddress.ContainsKey(address))
            .Select(address => new DiscoveryCandidate(address, transfer, InstructionSuccessorType.Normal,
                ExploreSource.TableEntry))
            .ToList();
        ExploreCandidates(transfer, candidates);
        if (!table.IsCall) {
            return;
        }
        // Indirect call: apply the call-continuation rule once per entry as explicit callee. The
        // first known-returning entry wires the continuation; others leave the site pending.
        Queue<ExploreItem> worklist = new();
        foreach (ushort entry in entries) {
            TryExploreCallContinuationForCallee(transfer, new SegmentedAddress(transfer.Address.Segment, entry), worklist);
        }
        Drain(worklist);
    }

    /// <summary>
    /// Reads the handler field of every record the recognized lists cover, under the site's
    /// current segment register value (counts in memory cells are read now, never at recognition
    /// time). Every observed Normal target of the site must be one of the read records, else the
    /// recognition is refused; the rest are proposed as <see cref="ExploreSource.RecordEntry"/>
    /// candidates under <paramref name="trigger"/>.
    /// </summary>
    private void EvaluateRecordArray(CfgInstruction site,
        RecordArrayRecognizer.RecordArrayShape shape, CfgInstruction trigger) {
        ushort segment = _state.SegmentRegisters.UInt16[(uint)shape.SegmentRegisterIndex];
        HashSet<SegmentedAddress> targets = [];
        foreach (RecordArrayRecognizer.RecordList list in shape.Lists) {
            ushort count = list.CountConstant ?? ReadSegmentWord(list.CountSegmentRegisterIndex,
                list.CountCellOffset);
            if (count > 256) {
                _loggerService.LogDebug("Record array at {Address}: list count {Count} over the 256 maximum",
                    site.Address, count);
                return;
            }
            for (int index = 0; index < count; index++) {
                uint at = (uint)(list.Base + index * list.Stride + shape.Displacement);
                SegmentedAddress? target = ReadRecordTarget(site.Address.Segment, segment, at,
                    shape.IsFar);
                if (target is not null) {
                    targets.Add(target.Value);
                }
            }
        }
        // Every observed jump/call target of the site must be one of the read records, else the
        // recognized lists do not describe this transfer (a path the walk did not track produced
        // the pointer).
        if (!site.SuccessorsPerType.TryGetValue(InstructionSuccessorType.Normal, out ISet<ICfgNode>? targetSuccessors)) {
            targetSuccessors = new HashSet<ICfgNode>();
        }
        foreach (ICfgNode successor in targetSuccessors) {
            if (!successor.IsSpeculative && !targets.Contains(successor.Address)) {
                _loggerService.LogDebug("Record array at {Address} refused: observed target {Target} is not a record",
                    site.Address, successor.Address);
                return;
            }
        }
        if (targets.Count > MaxCandidatesPerSite) {
            _loggerService.LogDebug("Record array at {Address} read {Count} targets, over the {Max} site maximum",
                site.Address, targets.Count, MaxCandidatesPerSite);
        }
        List<DiscoveryCandidate> candidates = targets
            .Where(address => !site.SuccessorsPerAddress.ContainsKey(address))
            .Select(address => new DiscoveryCandidate(address, site, InstructionSuccessorType.Normal,
                ExploreSource.RecordEntry))
            .ToList();
        ExploreCandidates(trigger, candidates);
        if (!site.IsCall) {
            return;
        }
        Queue<ExploreItem> worklist = new();
        foreach (SegmentedAddress target in targets) {
            TryExploreCallContinuationForCallee(site, target, worklist);
        }
        Drain(worklist);
    }

    private SegmentedAddress? ReadRecordTarget(ushort siteSegment, ushort segment, uint at,
        bool isFar) {
        ushort offset = ReadSegmentWord(segment, at);
        if (!isFar) {
            return offset is 0x0000 or 0xFFFF ? null : new SegmentedAddress(siteSegment, offset);
        }
        ushort targetSegment = ReadSegmentWord(segment, at + 2);
        return offset is 0x0000 or 0xFFFF || targetSegment is 0x0000 or 0xFFFF
            ? null
            : new SegmentedAddress(targetSegment, offset);
    }

    private ushort ReadSegmentWord(int segmentRegisterIndex, uint offset) {
        ushort segment = _state.SegmentRegisters.UInt16[(uint)segmentRegisterIndex];
        return ReadSegmentWord(segment, offset);
    }

    private ushort ReadSegmentWord(ushort segment, uint offset) =>
        (ushort)(SneakilyReadCodeByte(segment, offset) | SneakilyReadCodeByte(segment, offset + 1) << 8);

    /// <summary>
    /// The call-continuation rule with an explicitly given callee entry, for callers that know
    /// the callee without a static successor on the call node (table entries now, other sources
    /// later). Enqueues the continuation when the callee is known to return, else records the call
    /// site pending on it.
    /// </summary>
    internal void ExploreCallContinuationForCallee(CfgInstruction callNode, SegmentedAddress calleeEntry) {
        Queue<ExploreItem> worklist = new();
        TryExploreCallContinuationForCallee(callNode, calleeEntry, worklist);
        Drain(worklist);
    }

    private void TryExploreCallContinuationForCallee(CfgInstruction callNode, SegmentedAddress calleeEntry, Queue<ExploreItem> worklist) {
        if (!_nodeIndex.Contains(callNode) || !IsUsable(callNode) || HasCallToReturnSuccessor(callNode)) {
            return;
        }
        if (_calleeReturnEvidence.IsKnownToReturn(calleeEntry) || IsProvenFor(calleeEntry, callNode)) {
            EnqueueContinuation(callNode, worklist, InstructionSuccessorType.CallToReturn, false, ExploreSource.CallContinuation, 0, null, null);
        } else {
            AddPendingCallSite(calleeEntry, callNode, 0, null);
        }
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
                candidate.EdgeType, FollowContinuations: false, candidate.Source, trigger, 0, null,
                GuessRoot: null));
        }
        Drain(worklist);
        if (pending.Count == 0) {
            _pending.Remove(trigger);
        }
    }

    /// <summary>
    /// Dump-time completion: retries every data-sourced candidate still pending on its trigger
    /// (the trust check decodes only those whose bytes hold their loaded value again; the rest
    /// stay pending), and evaluates every far watched cell whose second word never came with the
    /// value in memory now, through its dependents.
    /// </summary>
    internal void RetryAllPending() {
        _watchedCells.EvaluatePartiallyWritten();
        foreach (CfgInstruction trigger in _pending.Keys.ToList()) {
            RetryPending(trigger);
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
            candidate.EdgeType, FollowContinuations: false, candidate.Source, trigger, 0, null,
            GuessRoot: null));
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
            _loggerService.LogDebug(
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
        trigger.HasDiscoveryDependents = true;
    }

    private static bool IsDataSource(ExploreSource source) => source is
        ExploreSource.TableEntry or ExploreSource.CatalogueEntry or ExploreSource.InterruptVector
        or ExploreSource.RegisterValue or ExploreSource.PointerValue or ExploreSource.RecordEntry;

    private static bool IsGuess(ExploreSource source) =>
        IsDataSource(source) || source is ExploreSource.PushedReturn;

    private static bool IsRefused(SegmentedAddress target) =>
        target.Offset is 0x0000 or 0xFFFF || target.Segment is 0x0000 or 0xFFFF;

    /// <summary>
    /// Every byte of the parsed instruction still holds the value its loader wrote. The physical
    /// address is computed like a code fetch (MMU translation then A20 gate) so the check lines up
    /// with the bytes the CPU would execute.
    /// </summary>
    private bool BytesHoldLoadedValue(CfgInstruction candidate) {
        for (int i = 0; i < candidate.Length; i++) {
            uint physical = _memory.A20Gate.TransformAddress(
                _memory.Mmu.TranslateAddress(candidate.Address.Segment, (uint)candidate.Address.Offset + (uint)i));
            if (!_changedMemoryMap.HoldsLoadedValue(physical)) {
                return false;
            }
        }
        return true;
    }

    // Iteratively decodes and links all reachable instructions from the worklist. When the
    // outermost drain completes, call sites still pending on their callee get one proof round:
    // the callee-return scan runs only here, never per item.
    private void Drain(Queue<ExploreItem> worklist) {
        _drainNesting++;
        try {
            DrainLoop(worklist);
        } finally {
            if (--_drainNesting == 0) {
                SweepWrongRoots();
                ScanPendingCallCallees();
            }
        }
    }

    /// <summary>
    /// An invalid decode condemns the guess only on evidence the guess produced: a Normal-edge item
    /// (fall-through, jump or conditional branch; call/int targets and call continuations cross
    /// into code the guess does not own) whose bytes still hold their loaded value.
    /// </summary>
    private bool IsWrongGuessEvidence(ExploreItem item, CfgInstruction invalid) =>
        item.EdgeType == InstructionSuccessorType.Normal
            && item.Predecessor is { IsCall: false }
            && BytesHoldLoadedValue(invalid);

    /// <summary>
    /// Sweeps every guess root the drain condemned through an invalid decode of its own code. A
    /// root is skipped when it left the index, stopped being speculative, or gained a predecessor
    /// other than its own guess site since it was condemned. Swept roots are remembered by
    /// (address, signature) so a site re-submitting the same bytes decodes nothing.
    /// </summary>
    private void SweepWrongRoots() {
        if (_wrongRoots.Count == 0) {
            return;
        }
        List<(CfgInstruction Root, SegmentedAddress InvalidAddress)> wrongRoots = new(_wrongRoots);
        _wrongRoots.Clear();
        foreach ((CfgInstruction root, SegmentedAddress invalidAddress) in wrongRoots) {
            _rootGuessSite.TryGetValue(root, out CfgInstruction? guessSite);
            if (!_nodeIndex.Contains(root) || !root.IsSpeculative) {
                continue;
            }
            if (root.Predecessors.Any(predecessor => !ReferenceEquals(predecessor, guessSite))) {
                continue;
            }
            _rejectedGuesses.Add((root.Address, root.SignatureFinal));
            _loggerService.LogDebug("Guessed root {Address} swept: its own code reached invalid bytes at {Invalid}",
                root.Address, invalidAddress);
            _pruner.Sweep(root);
        }
    }

    private void DrainLoop(Queue<ExploreItem> worklist) {
        while (worklist.Count > 0) {
            ExploreItem item = worklist.Dequeue();
            SegmentedAddress address = item.Target;
            if (_nodeIndex.PoisonSet.Contains(address)) {
                continue;
            }
            CfgInstruction candidate = _instructionParser.ParseInstructionAt(address);
            if (item.GuessRoot is null && IsGuess(item.Source)
                && _rejectedGuesses.Contains((address, candidate.SignatureFinal))) {
                // A guess already swept once is not decoded again while its bytes are unchanged.
                continue;
            }
            if (candidate.IsInvalid) {
                if (item.GuessRoot is not null && IsWrongGuessEvidence(item, candidate)) {
                    _wrongRoots.Add((item.GuessRoot, address));
                }
                // Only static-edge exploration poisons, and only when the item does not descend
                // from a guessed root: a guess's descendants are dropped so the address can be
                // discovered again once its bytes mean something.
                if (item.Source == ExploreSource.Static && item.GuessRoot is null) {
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
                TryRegisterAnchorSite(item, existing);
                // Anchored items keep walking through already-decoded speculative nodes so
                // sites deeper than the convergence frontier are still reached. A node is
                // re-walked only when the item knows a register no earlier traversal knew:
                // re-walking with a subset of the union would enqueue nothing new.
                if (item.Anchor is not null && existing.IsSpeculative) {
                    _anchorKnownUnion.TryGetValue(existing, out ushort union);
                    if ((item.KnownRegisters & ~union) != 0) {
                        _anchorKnownUnion[existing] = (ushort)(union | item.KnownRegisters);
                        EnqueueSuccessorsForNode(existing, worklist, item);
                    }
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
            _drainInserted = true;
            if (item.GuessRoot is null && IsGuess(item.Source)) {
                _rootGuessSite[candidate] = item.Predecessor;
            }
            if (item.Predecessor is not null) {
                _nodeLinker.Link(item.EdgeType, item.Predecessor, candidate);
            }
            TryRegisterAnchorSite(item, candidate);
            EnqueueSuccessorsForNode(candidate, worklist, item);
            if (item.Anchor is not null) {
                _anchorKnownUnion[candidate] = item.KnownRegisters;
            }
            if (item.Predecessor is null && item.Source is ExploreSource.CatalogueEntry or ExploreSource.InterruptVector) {
                // A seed with no successors (a lone ret, for instance) never goes through Link,
                // so nothing opens its block. The partition collector needs one; known-safe
                // handler seeds deliberately stay blockless until something links them.
                _nodeLinker.EnsureBlock(candidate);
                SpeculativeSeedCreated?.Invoke(candidate);
            }
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
    private void EnqueueSuccessorsForNode(CfgInstruction node, Queue<ExploreItem> worklist, ExploreItem item) {
        bool followContinuations = item.FollowContinuations;
        // The guess root of every item below this node: the node itself when it is the root a
        // guess-source item just inserted, else the root the item already descended from.
        CfgInstruction? guessRoot = item.GuessRoot ?? (IsGuess(item.Source) ? node : null);
        // The register values the anchor's state can still predict past this node: everything the
        // node itself does not write.
        ushort nextKnown = (ushort)(item.KnownRegisters & ~RegisterWriteSet.MayWrite(node));
        // Nothing left that the anchor's state can predict: the items below can no longer
        // resolve a site, so the anchor stops here instead of growing the re-traversal.
        CfgInstruction? nextAnchor = nextKnown == 0 ? null : item.Anchor;
        if (node.IsCall) {
            // Call or INT: static successors are callees (enqueued untrusted, always Normal edges).
            // Continuation is only enqueued if under trust, or when the callee is known to return.
            foreach (SegmentedAddress successor in node.StaticSuccessorAddresses.Where(address => !_nodeIndex.PoisonSet.Contains(address))) {
                worklist.Enqueue(new ExploreItem(successor, node, InstructionSuccessorType.Normal, followContinuations, ExploreSource.Static, null, nextKnown, nextAnchor, guessRoot));
            }
            if (followContinuations) {
                EnqueueContinuation(node, worklist, InstructionSuccessorType.CallToReturn, followContinuations, ExploreSource.Static, 0, null, guessRoot);
            } else {
                // The registers known right before the call, and the anchor they come from:
                // the continuation keeps whichever of them the callee does not write.
                TryExploreCallContinuation(node, worklist, item.KnownRegisters, item.Anchor, guessRoot);
            }
            return;
        }
        if (node.StaticSuccessorAddresses.Count > 0) {
            // Non-call instructions with known static successors: propagate trust through intra-routine successors.
            // Static successors are always Normal edges.
            EnqueueStaticSuccessors(node, worklist, followContinuations, nextKnown, nextAnchor, guessRoot);
            return;
        }
        // No static successors and not a call: either a callback instruction, a RET/IRET, or an indirect jump.
        // Only callback instructions have a meaningful memory continuation; follow it under trust.
        if (followContinuations && node.MaxSuccessorsCount is null && !node.IsReturn && !node.IsJump && !node.IsInvalid) {
            EnqueueContinuation(node, worklist, InstructionSuccessorType.Normal, followContinuations, ExploreSource.Static, 0, null, guessRoot);
        }
        TryExplorePushedReturn(node, worklist);
    }

    /// <summary>
    /// When an indirect near jump's block touches the stack
    /// exactly once between its leader and the jump, and that instruction is <c>push imm16</c> or
    /// <c>push r16</c> last set by <c>mov r16, imm16</c>, the pushed address is the return address
    /// a hand-built call's callee lands on. Enqueue it as a <see cref="ExploreSource.PushedReturn"/>
    /// item with a Normal edge on the jump (what an observed unmatched ret records); the generated
    /// ret only pops and dispatches, so no generator change is needed.
    /// </summary>
    private void TryExplorePushedReturn(CfgInstruction node, Queue<ExploreItem> worklist) {
        if (!PushedAddressMatcher.IsIndirectNearJump(node)
            || !IsUsable(node)
            || node.ContainingBlock is null) {
            return;
        }
        List<CfgInstruction> prior = new();
        foreach (ICfgNode blockNode in node.ContainingBlock.Instructions) {
            if (ReferenceEquals(blockNode, node)) {
                break;
            }
            if (blockNode is CfgInstruction instruction) {
                prior.Add(instruction);
            }
        }
        List<CfgInstruction> stackTouches = prior.Where(PushedAddressMatcher.TouchesStack).ToList();
        if (stackTouches.Count != 1) {
            return;
        }
        CfgInstruction push = stackTouches[0];
        if (!PushedAddressMatcher.TryMatchPush(push, out int pushedRegister, out ushort immediate)) {
            return;
        }
        if (pushedRegister >= 0) {
            List<CfgInstruction> newestFirst = prior.TakeWhile(instruction => !ReferenceEquals(instruction, push)).Reverse<CfgInstruction>().ToList();
            if (PushedAddressMatcher.ResolvePushedImmediate(pushedRegister, newestFirst) is not ushort resolved) {
                return;
            }
            immediate = resolved;
        }
        SegmentedAddress target = new(node.Address.Segment, immediate);
        if (_nodeIndex.HasAddress(target)) {
            // The address is already decoded; wiring the jump to it would only add a possibly
            // cyclic edge (e.g. `push r16; jmp sp` where SP was set by an immediate move).
            return;
        }
        worklist.Enqueue(new ExploreItem(
            target, node, InstructionSuccessorType.Normal, FollowContinuations: false,
            ExploreSource.PushedReturn, null, 0, null, GuessRoot: null));
    }

    /// <summary>
    /// When a speculative node reached from an anchor is an indirect near or far jmp/call whose
    /// operand reads only registers still known from the anchor's state, register it as a dependent
    /// on the anchor so each anchor execution proposes its current target. Anything outside the
    /// handled shapes (register or segment unknown, base+index, scaled forms) is refused.
    /// </summary>
    private void TryRegisterAnchorSite(ExploreItem item, CfgInstruction site) {
        // Every anchor whose known registers cover the operand may register its own dependent:
        // each anchor's executions evaluate the same operand at a different observed point on the
        // path, and the per-site target set deduplicates the values they propose. A refusal under
        // one anchor is not recorded: a later anchor may know more registers.
        if (item.Anchor is null || !site.IsSpeculative || _anchorSitePairs.Contains((site, item.Anchor))) {
            return;
        }
        if (site.DisplayAst is not InstructionNode { Parameters: [var parameter] } display
            || display.Operation is not (InstructionOperation.JMP_NEAR or InstructionOperation.CALL_NEAR
                or InstructionOperation.JMP_FAR or InstructionOperation.CALL_FAR)) {
            return;
        }
        bool isCall = display.Operation is InstructionOperation.CALL_NEAR or InstructionOperation.CALL_FAR;
        bool isFar = display.Operation is InstructionOperation.JMP_FAR or InstructionOperation.CALL_FAR;
        if (!isFar && SwitchTableRecognizer.AsRegister(parameter) is { DataType.BitWidth: BitWidth.WORD_16 } register) {
            if (!IsKnown(item.KnownRegisters, register.RegisterIndex)) {
                _refusedAnchorSiteForms++;
                return;
            }
            AddDependent(item.Anchor, new RegisterDependent(site, isCall, register.RegisterIndex));
            return;
        }
        if (parameter is not SegmentedPointerNode pointer
            || SwitchTableRecognizer.GetSegmentRegisterIndex(pointer) is not int segmentRegisterIndex
            || !IsKnown(item.KnownRegisters, 8 + segmentRegisterIndex)) {
            _refusedAnchorSiteForms++;
            return;
        }
        int expectedSize = isFar ? 4 : 2;
        if (pointer.DataType.BitWidth is not (BitWidth.WORD_16 or BitWidth.DWORD_32)
            || pointer.DataType.BitWidth != (isFar ? BitWidth.DWORD_32 : BitWidth.WORD_16)) {
            _refusedAnchorSiteForms++;
            return;
        }
        if (SwitchTableRecognizer.AsConstant(pointer.Offset) is ulong constOffset) {
            if (constOffset > 0xFFFF) {
                _refusedAnchorSiteForms++;
                return;
            }
            AddDependent(item.Anchor, new CellDependent(site, isCall, expectedSize, segmentRegisterIndex, null, (ushort)constOffset));
            return;
        }
        if (!SwitchTableRecognizer.TryGetIndexExpression(pointer.Offset, out int indexRegister, out ushort displacement)) {
            _refusedAnchorSiteForms++;
            return;
        }
        if (!IsKnown(item.KnownRegisters, indexRegister)) {
            // Bounded indexed-table form: the backward walk supplies the reachable entry offsets,
            // so the index register itself need not be known, only the segment.
            if (!isFar && SwitchTableRecognizer.RecognizeSpeculative(site) is { } table
                && table.Bound + 1 <= MaxTableEntries) {
                AddDependent(item.Anchor, new TableDependent(site, isCall, segmentRegisterIndex,
                    table.BaseOffset, table.Step, table.Bound));
                return;
            }
            // Index register unknown: the record-array walk may still prove which records it can
            // point at (near or far sites).
            if (TryRecordArrayDependent(item, site, isCall) is { } unknownIndexDependent) {
                AddDependent(item.Anchor, unknownIndexDependent);
                return;
            }
            _refusedAnchorSiteForms++;
            return;
        }
        AddDependent(item.Anchor, new CellDependent(site, isCall, expectedSize, segmentRegisterIndex, indexRegister, displacement));
        // The cell alone gives one record's handler; the array gives them all.
        if (TryRecordArrayDependent(item, site, isCall) is { } recordDependent) {
            AddDependent(item.Anchor, recordDependent);
        }
    }

    /// <summary>Recognizes the site as a record array for anchor registration: lists whose count
    /// cell lives in a segment register the anchor does not know are dropped.</summary>
    private RecordArrayDependent? TryRecordArrayDependent(ExploreItem item, CfgInstruction site, bool isCall) {
        if (_recordArrayRecognizer.Recognize(site) is not { } shape) {
            return null;
        }
        List<RecordArrayRecognizer.RecordList> lists = shape.Lists
            .Where(list => list.CountConstant is not null
                || IsKnown(item.KnownRegisters, 8 + list.CountSegmentRegisterIndex))
            .ToList();
        if (lists.Count == 0) {
            return null;
        }
        return new RecordArrayDependent(site, isCall,
            shape with { Lists = lists });
    }

    private void AddDependent(CfgInstruction anchor, AnchorDependent dependent) {
        _anchorSitePairs.Add((dependent.Site, anchor));
        if (!_anchorDependents.TryGetValue(anchor, out List<AnchorDependent>? dependents)) {
            dependents = _anchorDependents[anchor] = [];
        }
        dependents.Add(dependent);
        anchor.HasDiscoveryDependents = true;
    }

    private static bool IsKnown(ushort known, int bitIndex) => (known & (1 << bitIndex)) != 0;

    /// <summary>
    /// Called on every execution of a node that carries speculative-site dependents: retries the
    /// candidates pending on it, then proposes each dependent's current value - a register target
    /// (CS:register for a near site) or a pointer-cell target (CS:word for a near cell, the cell's
    /// own segment word for a far one). Cell reads also arm a watched cell so a later program
    /// write proposes the new value under the writing instruction as trigger.
    /// </summary>
    public void EvaluateAnchorDependents(CfgInstruction anchor) {
        // A restored node owes its anchor walk: run it once at first execution, then let the
        // normal evaluation below propose what the freshly registered dependents read.
        bool firstWalk = _pendingAnchorWalks.Remove(anchor);
        if (firstWalk) {
            ExploreFrom(anchor);
        }
        RetryPending(anchor);
        if (_anchorDependents.TryGetValue(anchor, out List<AnchorDependent>? dependents)) {
            foreach (AnchorDependent dependent in dependents) {
                EvaluateDependent(anchor, dependent);
            }
        }
        // The walk was a one-shot cost: a reloaded instruction that claimed nothing must not keep
        // paying the per-execution test. A later AddDependent/AddPending re-arms the flag itself.
        if (firstWalk && !_anchorDependents.ContainsKey(anchor) && !_pending.ContainsKey(anchor)) {
            anchor.HasDiscoveryDependents = false;
        }
    }

    private void EvaluateDependent(CfgInstruction anchor, AnchorDependent dependent) {
        switch (dependent) {
            case TableDependent table:
                EvaluateTableDependent(anchor, table);
                return;
            case RecordArrayDependent recordArray:
                EvaluateRecordArray(recordArray.Site, recordArray.Shape, anchor);
                return;
            case RegisterDependent register: {
                ushort offset = _state.GeneralRegisters.UInt16[(uint)register.Register];
                SubmitAnchorCandidate(anchor, register,
                    new SegmentedAddress(register.Site.Address.Segment, offset), ExploreSource.RegisterValue);
                return;
            }
            case CellDependent cell: {
                ushort segment = _state.SegmentRegisters.UInt16[(uint)cell.SegmentRegisterIndex];
                uint cellOffset = cell.IndexRegister is int indexRegister
                    ? (uint)(_state.GeneralRegisters.UInt16[(uint)indexRegister] + cell.Displacement)
                    : cell.Displacement;
                uint physical = _memory.A20Gate.TransformAddress(_memory.Mmu.TranslateAddress(segment, cellOffset));
                uint value = 0;
                for (int i = 0; i < cell.CellSize; i++) {
                    value |= (uint)_memory.SneakilyRead(physical + (uint)i) << (8 * i);
                }
                WatchSiteCell(dependent, cell.CellSize, physical);
                SubmitAnchorCandidate(anchor, cell, CellTarget(cell, value), ExploreSource.PointerValue);
                return;
            }
        }
    }

    /// <summary>
    /// Reads every entry of a bounded indexed table under the anchor's current segment value and
    /// proposes each as a candidate for the site. The walk's bound and step give the offsets, so
    /// the index register itself is never consulted.
    /// </summary>
    private void EvaluateTableDependent(CfgInstruction anchor, TableDependent dependent) {
        ushort segment = _state.SegmentRegisters.UInt16[(uint)dependent.SegmentRegisterIndex];
        HashSet<ushort> entries = new();
        for (int i = 0; i <= dependent.Bound; i++) {
            uint offset = (uint)(dependent.BaseOffset + dependent.Step * i);
            uint physical = _memory.A20Gate.TransformAddress(_memory.Mmu.TranslateAddress(segment, offset));
            WatchSiteCell(dependent, 2, physical);
            ushort entry = (ushort)(_memory.SneakilyRead(physical) | _memory.SneakilyRead(physical + 1) << 8);
            if (entry is 0x0000 or 0xFFFF) {
                return;
            }
            entries.Add(entry);
        }
        foreach (ushort entry in entries) {
            SubmitAnchorCandidate(anchor, dependent,
                new SegmentedAddress(dependent.Site.Address.Segment, entry), ExploreSource.TableEntry);
        }
    }

    private void WatchSiteCell(AnchorDependent dependent, int cellSize, uint physical) {
        if (!_watchedSiteCells.Add((physical, cellSize, dependent.Site))) {
            return;
        }
        _watchedCells.Watch(physical, cellSize, new WatchedPointerCells.WatchedCellDependent(
            (value, trigger) => OnSiteCellValue(dependent, value, trigger)));
    }

    private void OnSiteCellValue(AnchorDependent dependent, uint value, CfgInstruction? trigger) {
        // Watched cells cannot be unwatched, so a dependent whose site left the index is ignored.
        if (!_nodeIndex.Contains(dependent.Site)) {
            return;
        }
        ExploreCandidates(trigger,
            [new DiscoveryCandidate(CellTarget(dependent, value), dependent.Site,
                InstructionSuccessorType.Normal, ExploreSource.PointerValue)]);
        if (dependent.IsCall) {
            Queue<ExploreItem> worklist = new();
            TryExploreCallContinuationForCallee(dependent.Site, CellTarget(dependent, value), worklist);
            Drain(worklist);
        }
    }

    private static SegmentedAddress CellTarget(AnchorDependent dependent, uint cellValue) =>
        new(dependent is CellDependent { CellSize: 4 } ? (ushort)(cellValue >> 16) : dependent.Site.Address.Segment,
            (ushort)cellValue);

    private void SubmitAnchorCandidate(CfgInstruction anchor, AnchorDependent dependent,
        SegmentedAddress target, ExploreSource source) {
        ExploreCandidates(anchor,
            [new DiscoveryCandidate(target, dependent.Site, InstructionSuccessorType.Normal, source)]);
        if (dependent.IsCall) {
            Queue<ExploreItem> worklist = new();
            TryExploreCallContinuationForCallee(dependent.Site, target, worklist);
            Drain(worklist);
        }
    }

    /// <summary>
    /// Records one observed call-to-return-target link in <see cref="CalleeReturnEvidence"/> for the
    /// given callee entry, then drains the call sites pending on that callee
    /// when it becomes known to return for the first time.
    /// </summary>
    internal void RecordCallReturnEvidence(SegmentedAddress calleeEntry, bool aligned) {
        if (_calleeReturnEvidence.Record(calleeEntry, aligned)) {
            DrainPendingCallSites(calleeEntry, null);
        }
    }

    /// <summary>
    /// Rebuilds the return evidence index from a reloaded graph and re-runs the call-continuation
    /// rule on every reloaded call node, so continuations that a dump contains only as evidence
    /// are wired again without re-executing the program, and marks every restored observed
    /// instruction for the anchor walk it never got, run once at its first execution. Called once
    /// at the end of <c>CfgGraphReloader.Reload</c>.
    /// </summary>
    public void RebuildReturnEvidence(IEnumerable<ICfgNode> nodes) {
        List<CfgInstruction> callNodes = nodes.OfType<CfgInstruction>().Where(node => node.IsCall).ToList();
        foreach (CfgInstruction callNode in callNodes) {
            SegmentedAddress? callee = CalleeEntry(callNode);
            if (callee is null) {
                continue;
            }
            // Speculative wiring provenance is not reloaded: only edges landing on a non-speculative
            // target are observed evidence.
            foreach ((InstructionSuccessorType type, bool aligned) in ReturnEdgeTypes) {
                if (!callNode.SuccessorsPerType.TryGetValue(type, out ISet<ICfgNode>? targets)) {
                    continue;
                }
                if (targets.Any(target => !target.IsSpeculative)) {
                    _calleeReturnEvidence.Record(callee.Value, aligned);
                }
            }
        }
        Queue<ExploreItem> worklist = new();
        foreach (CfgInstruction callNode in callNodes) {
            TryExploreCallContinuation(callNode, worklist, 0, null, null);
        }
        Drain(worklist);
        // Restored observed nodes were never parsed in this run, so no anchor walk registered
        // their speculative-site dependents. Mark each for a one-shot walk at first execution;
        // speculative nodes are skipped because the walks of the observed ones already reach them.
        foreach (ICfgNode node in nodes) {
            if (node is CfgInstruction { IsSpeculative: false } instruction) {
                _pendingAnchorWalks.Add(instruction);
                instruction.HasDiscoveryDependents = true;
            }
        }
    }

    private static readonly (InstructionSuccessorType Type, bool Aligned)[] ReturnEdgeTypes = [
        (InstructionSuccessorType.CallToReturn, true),
        (InstructionSuccessorType.CallToMisalignedReturn, false),
        (InstructionSuccessorType.CallToRedirectedReturn, false)];

    private void OnCallReturnLinked(CfgInstruction call, InstructionSuccessorType type) {
        bool? aligned = type switch {
            InstructionSuccessorType.CallToReturn => true,
            InstructionSuccessorType.CallToMisalignedReturn => false,
            InstructionSuccessorType.CallToRedirectedReturn => false,
            _ => null
        };
        if (aligned is null) {
            return;
        }
        SegmentedAddress? callee = CalleeEntry(call);
        if (callee is not null) {
            RecordCallReturnEvidence(callee.Value, aligned.Value);
        }
    }

    /// <summary>
    /// The entry address of the callee this call or int node transfers to: the single static
    /// successor of a direct call, or the IVT entry of a software interrupt read from memory at
    /// the moment the fact is needed. Calls with no statically-known target are out of scope.
    /// </summary>
    private SegmentedAddress? CalleeEntry(CfgInstruction callNode) {
        if (callNode.StaticSuccessorAddresses.Count == 1) {
            return callNode.StaticSuccessorAddresses[0];
        }
        if (InterruptCallFacts.TryGetInterruptVector(callNode, out byte vector)) {
            return _interruptVectorTable[vector];
        }
        return null;
    }

    /// <summary>
    /// Enqueues the call/int continuation when the callee is known to return, or records the call
    /// node pending on its callee entry so it is retried when aligned return evidence appears.
    /// Nodes that left the index are skipped. When the item reaching the call carries an anchor,
    /// the continuation keeps the registers the callee write scan does not name, and keeps
    /// walking under that anchor even when the CallToReturn edge already exists.
    /// </summary>
    private void TryExploreCallContinuation(CfgInstruction callNode, Queue<ExploreItem> worklist,
        ushort knownBeforeCall, CfgInstruction? anchor, CfgInstruction? guessRoot) {
        if (!_nodeIndex.Contains(callNode) || !IsUsable(callNode)) {
            return;
        }
        ushort continuationKnown = anchor is null ? (ushort)0 : ContinuationKnown(callNode, knownBeforeCall);
        CfgInstruction? continuationAnchor = continuationKnown == 0 ? null : anchor;
        if (HasCallToReturnSuccessor(callNode)) {
            if (continuationAnchor is not null) {
                EnqueueContinuation(callNode, worklist, InstructionSuccessorType.CallToReturn, false,
                    ExploreSource.CallContinuation, continuationKnown, continuationAnchor, guessRoot);
            }
            return;
        }
        if (IsCalleeKnownToReturn(callNode, out SegmentedAddress? callee)) {
            EnqueueContinuation(callNode, worklist, InstructionSuccessorType.CallToReturn, false,
                ExploreSource.CallContinuation, continuationKnown, continuationAnchor, guessRoot);
        } else if (callee is not null) {
            AddPendingCallSite(callee.Value, callNode, knownBeforeCall, anchor);
        }
    }

    /// <summary>The registers of <paramref name="knownBeforeCall"/> that still hold after
    /// <paramref name="callNode"/> returns: the ones the callee write scan does not name. An
    /// unknown callee clears everything, exactly like before the scan existed.</summary>
    private ushort ContinuationKnown(CfgInstruction callNode, ushort knownBeforeCall) {
        if (knownBeforeCall == 0) {
            return 0;
        }
        ushort mask = _calleeWriteScanner.MayWriteAcross(callNode);
        if (mask == (ushort)RegisterBits.AllExceptCs) {
            return 0;
        }
        return (ushort)(knownBeforeCall & ~mask);
    }

    private bool IsCalleeKnownToReturn(CfgInstruction callNode, out SegmentedAddress? callee) {
        callee = CalleeEntry(callNode);
        if (callee is null) {
            return false;
        }
        if (InterruptCallFacts.TryGetInterruptVector(callNode, out byte vector)
            && _installedInterruptHandlers.IsInstalledHandler(callee.Value)) {
            // An int that still targets an emulator-installed handler returns, except the
            // DOS termination services.
            if (vector is 0x20 or 0x27) {
                return false;
            }
            return vector != 0x21 || InterruptCallFacts.AhLoadedBefore(callNode) is not (0x00 or 0x31 or 0x4C);
        }
        if (_calleeReturnEvidence.IsKnownToReturn(callee.Value)) {
            return true;
        }
        // A callee the scan proved to return also qualifies, for sites of the distance its
        // endings are uniform on - unless observed unaligned evidence has disproved it since.
        return IsProvenFor(callee.Value, callNode);
    }

    private bool IsProvenFor(SegmentedAddress calleeEntry, CfgInstruction callNode) =>
        _provedReturnKind.TryGetValue(calleeEntry, out CallDistance kind)
            && kind == SiteDistance(callNode)
            && !_calleeReturnEvidence.IsDisproved(calleeEntry);

    /// <summary>The call/return distance of a call or int node.</summary>
    private static CallDistance SiteDistance(CfgInstruction callNode) => callNode.ExecutionAst switch {
        CallFarNode => CallDistance.Far,
        InterruptCallNode => CallDistance.Interrupt,
        _ => CallDistance.Near
    };

    /// <summary>
    /// Runs the callee-return scan on every callee that still has pending call sites and is not
    /// already proven or refused. Called only when the outermost <see cref="Drain"/> completes;
    /// a callee refused because its subgraph was incomplete is rescanned only after a drain
    /// inserted nodes. A proof drains the pending sites of the matching distance, which may
    /// insert nodes and let further callees prove in the same round.
    /// </summary>
    /// <summary>Whether the callee at <paramref name="entry"/> is known to return with its own
    /// stack balanced: observed return evidence, or a proof the evidence has not disproved
    /// since. Passed to <see cref="CalleeWriteScanner"/> at construction so the SP rule reads
    /// the evidence as it stands at each query.</summary>
    private bool ReturnsBalanced(SegmentedAddress entry) =>
        _calleeReturnEvidence.IsKnownToReturn(entry)
        || (_provedReturnKind.ContainsKey(entry) && !_calleeReturnEvidence.IsDisproved(entry));

    private void ScanPendingCallCallees() {
        if (_pendingCallSites.Count == 0) {
            return;
        }
        bool progress = true;
        while (progress) {
            progress = false;
            foreach (SegmentedAddress callee in _pendingCallSites.Keys.ToList()) {
                if (_provedReturnKind.ContainsKey(callee) || _refusedCalleeProof.Contains(callee)
                    || _calleeReturnEvidence.IsDisproved(callee)
                    || (_incompleteCalleeProof.Contains(callee) && !_drainInserted)) {
                    continue;
                }
                _incompleteCalleeProof.Remove(callee);
                switch (_calleeReturnScanner.Scan(callee, node => IsCalleeKnownToReturn(node, out _))) {
                    case CalleeReturnScanner.Outcome.Near:
                        ProveCallee(callee, CallDistance.Near);
                        progress = true;
                        break;
                    case CalleeReturnScanner.Outcome.Far:
                        ProveCallee(callee, CallDistance.Far);
                        progress = true;
                        break;
                    case CalleeReturnScanner.Outcome.Interrupt:
                        ProveCallee(callee, CallDistance.Interrupt);
                        progress = true;
                        break;
                    case CalleeReturnScanner.Outcome.Incomplete:
                        _incompleteCalleeProof.Add(callee);
                        break;
                    default:
                        _refusedCalleeProof.Add(callee);
                        break;
                }
            }
            _drainInserted = false;
        }
    }

    /// <summary>Records <paramref name="kind"/> as the return distance the scan proved for
    /// <paramref name="callee"/> and drains the pending call sites of that distance.</summary>
    private void ProveCallee(SegmentedAddress callee, CallDistance kind) {
        _provedReturnKind[callee] = kind;
        _loggerService.LogDebug("Callee {Address} proven to return ({Kind}) by its speculative subgraph",
            callee, kind);
        DrainPendingCallSites(callee, kind);
    }

    // A call site is usable while it is a live observed instruction or still a speculative
    // node in the index (a speculative instruction is never IsLive; pruning removes it from the
    // index). Dead observed nodes fail both tests.
    private static bool IsUsable(CfgInstruction node) => node.IsLive || node.IsSpeculative;

    private static bool HasCallToReturnSuccessor(CfgInstruction callNode) =>
        callNode.SuccessorsPerType.TryGetValue(InstructionSuccessorType.CallToReturn, out ISet<ICfgNode>? targets)
            && targets.Count > 0;

    private void AddPendingCallSite(SegmentedAddress callee, CfgInstruction callNode,
        ushort knownBeforeCall, CfgInstruction? anchor) {
        if (!_pendingCallSites.TryGetValue(callee, out List<PendingCallSite>? sites)) {
            sites = _pendingCallSites[callee] = [];
        }
        PendingCallSite? record = sites.Find(site => ReferenceEquals(site.Call, callNode));
        if (record is null) {
            record = new PendingCallSite(callNode, []);
            sites.Add(record);
        }
        if (anchor is not null && !record.Anchors.Contains((knownBeforeCall, anchor))) {
            record.Anchors.Add((knownBeforeCall, anchor));
        }
    }

    /// <summary>
    /// The callee just became known to return: wire the continuation of every pending call site
    /// still in the index. Sites pruned from the index are skipped; a site with no CallToReturn
    /// successor gets the plain continuation, and each anchor pair that reached it resumes its
    /// walk with the registers the callee does not write (computable now that the proof decoded
    /// the callee's subgraph).
    /// </summary>
    /// <param name="callee">The callee entry the pending sites call.</param>
    /// <param name="provenDistance">The proven return distance, or null when the evidence is an
    /// observed return that applies to every distance: a proof is uniform per distance, so sites
    /// of another distance stay pending.</param>
    private void DrainPendingCallSites(SegmentedAddress callee, CallDistance? provenDistance) {
        if (!_pendingCallSites.TryGetValue(callee, out List<PendingCallSite>? sites)) {
            return;
        }
        // A proof is uniform per distance: sites of another distance stay pending.
        List<PendingCallSite> matching = sites
            .Where(site => provenDistance is null || SiteDistance(site.Call) == provenDistance.Value)
            .ToList();
        sites.RemoveAll(matching.Contains);
        if (sites.Count == 0) {
            _pendingCallSites.Remove(callee);
        }
        Queue<ExploreItem> worklist = new();
        foreach (PendingCallSite site in matching) {
            CfgInstruction call = site.Call;
            if (!_nodeIndex.Contains(call) || !IsUsable(call)) {
                continue;
            }
            if (!HasCallToReturnSuccessor(call)) {
                EnqueueContinuation(call, worklist, InstructionSuccessorType.CallToReturn, false, ExploreSource.CallContinuation, 0, null, null);
            }
            foreach ((ushort knownBeforeCall, CfgInstruction anchor) in site.Anchors) {
                ushort known = ContinuationKnown(call, knownBeforeCall);
                if (known != 0) {
                    EnqueueContinuation(call, worklist, InstructionSuccessorType.CallToReturn, false, ExploreSource.CallContinuation, known, anchor, null);
                }
            }
        }
        Drain(worklist);
    }

    /// <summary>
    /// Enqueues the call/int continuation address (NextInMemoryAddress32) as an item of the given
    /// edge type and source. The continuation address is computed from instruction length, never
    /// by decoding the operand.
    /// </summary>
    private void EnqueueContinuation(CfgInstruction callNode, Queue<ExploreItem> worklist, InstructionSuccessorType edgeType, bool followContinuations, ExploreSource source, ushort knownRegisters, CfgInstruction? anchor, CfgInstruction? guessRoot) {
        SegmentedAddress continuation = callNode.NextInMemoryAddress32.ToSegmentedAddress();
        if (_nodeIndex.PoisonSet.Contains(continuation)) {
            return;
        }
        worklist.Enqueue(new ExploreItem(continuation, callNode, edgeType, followContinuations, source, null, knownRegisters, anchor, guessRoot));
    }

    private void EnqueueStaticSuccessors(CfgInstruction instruction, Queue<ExploreItem> worklist, bool followContinuations, ushort knownRegisters, CfgInstruction? anchor, CfgInstruction? guessRoot) {
        foreach (SegmentedAddress successor in instruction.StaticSuccessorAddresses.Where(address => !_nodeIndex.PoisonSet.Contains(address))) {
            worklist.Enqueue(new ExploreItem(successor, instruction, InstructionSuccessorType.Normal, followContinuations, ExploreSource.Static, null, knownRegisters, anchor, guessRoot));
        }
    }
}
