namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
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
    private readonly Dictionary<SegmentedAddress, List<CfgInstruction>> _pendingCallSites = new();
    private readonly HashSet<CfgInstruction> _cappedSites = [];

    public SpeculativeExplorer(IMemory memory, InstructionParser instructionParser, CfgNodeIndex nodeIndex, NodeLinker nodeLinker,
        ChangedMemoryMap changedMemoryMap, InstalledInterruptHandlers installedInterruptHandlers,
        InterruptVectorTable interruptVectorTable, FunctionCatalogue functionCatalogue,
        State state, ILogger loggerService) {
        _memory = memory;
        _state = state;
        _instructionParser = instructionParser;
        _nodeIndex = nodeIndex;
        _nodeLinker = nodeLinker;
        _changedMemoryMap = changedMemoryMap;
        _installedInterruptHandlers = installedInterruptHandlers;
        _interruptVectorTable = interruptVectorTable;
        _functionCatalogue = functionCatalogue;
        _loggerService = loggerService;
        _nodeLinker.CallReturnLinked += OnCallReturnLinked;
        // Catalogue entries are trust-checked against the loaded bytes, so they can only be
        // submitted after the image capture ran. ImageCaptured fires at the end of that capture,
        // which makes the ordering explicit instead of relying on event subscription order.
        _changedMemoryMap.ImageCaptured += OnImageCaptured;
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
        EnqueueStaticSuccessors(observed, worklist, followContinuations: false);
        if (observed.IsCall) {
            TryExploreCallContinuation(observed, worklist);
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
    /// Called on every execution of an observed near indirect jmp/call through a word memory
    /// operand. Retries the data-sourced candidates pending on it, and when the execution linked a
    /// target edge never seen before, resolves the switch/call table the transfer may read: reads
    /// the N+1 entries at the pointer's segment and displacement, refuses the table when an entry
    /// is a refused value or an observed target is missing from it, else submits every entry as a
    /// <see cref="ExploreSource.TableEntry"/> candidate. For an indirect call the
    /// call-continuation rule is then applied with each entry as an explicit callee.
    /// </summary>
    public void ExploreIndirectTransfer(CfgInstruction transfer, bool newTargetEdge) {
        RetryPending(transfer);
        if (!newTargetEdge) {
            return;
        }
        SwitchTableRecognizer.TableShape? table = _tableRecognizer.Recognize(transfer);
        if (table is null) {
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
            ushort offset = (ushort)(table.TableOffset + 2 * i);
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
        if (_calleeReturnEvidence.IsKnownToReturn(calleeEntry)) {
            EnqueueContinuation(callNode, worklist, InstructionSuccessorType.CallToReturn, false, ExploreSource.CallContinuation);
        } else {
            AddPendingCallSite(calleeEntry, callNode);
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
    private void EnqueueSuccessorsForNode(CfgInstruction node, Queue<ExploreItem> worklist, bool followContinuations) {
        if (node.IsCall) {
            // Call or INT: static successors are callees (enqueued untrusted, always Normal edges).
            // Continuation is only enqueued if under trust, or when the callee is known to return.
            foreach (SegmentedAddress successor in node.StaticSuccessorAddresses.Where(address => !_nodeIndex.PoisonSet.Contains(address))) {
                worklist.Enqueue(new ExploreItem(successor, node, InstructionSuccessorType.Normal, followContinuations, ExploreSource.Static, null));
            }
            if (followContinuations) {
                EnqueueContinuation(node, worklist, InstructionSuccessorType.CallToReturn, followContinuations, ExploreSource.Static);
            } else {
                TryExploreCallContinuation(node, worklist);
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
            EnqueueContinuation(node, worklist, InstructionSuccessorType.Normal, followContinuations, ExploreSource.Static);
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
            ExploreSource.PushedReturn, null));
    }

    /// <summary>
    /// Records one observed call-to-return-target link in <see cref="CalleeReturnEvidence"/> for the
    /// given callee entry, then drains the call sites pending on that callee
    /// when it becomes known to return for the first time.
    /// </summary>
    internal void RecordCallReturnEvidence(SegmentedAddress calleeEntry, bool aligned) {
        if (_calleeReturnEvidence.Record(calleeEntry, aligned)) {
            DrainPendingCallSites(calleeEntry);
        }
    }

    /// <summary>
    /// Rebuilds the return evidence index from a reloaded graph and re-runs the call-continuation
    /// rule on every reloaded call node, so continuations that a dump contains only as evidence
    /// are wired again without re-executing the program. Called once at the end of
    /// <c>CfgGraphReloader.Reload</c>.
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
            TryExploreCallContinuation(callNode, worklist);
        }
        Drain(worklist);
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
        if (TryGetInterruptVector(callNode, out byte vector)) {
            return _interruptVectorTable[vector];
        }
        return null;
    }

    private static bool TryGetInterruptVector(CfgInstruction node, out byte vector) {
        vector = 0;
        if (node.ExecutionAst is not InterruptCallNode call) {
            return false;
        }
        ulong? number = call.VectorNumber switch {
            ConstantNode constant => constant.Value,
            InstructionFieldNode field => field.ConstantValue,
            _ => null
        };
        if (number is null) {
            return false;
        }
        vector = (byte)number.Value;
        return true;
    }

    /// <summary>
    /// Enqueues the call/int continuation when the callee is known to return, or records the call
    /// node pending on its callee entry so it is retried when aligned return evidence appears.
    /// Nodes that already carry a CallToReturn successor or left the index are skipped.
    /// </summary>
    private void TryExploreCallContinuation(CfgInstruction callNode, Queue<ExploreItem> worklist) {
        if (!_nodeIndex.Contains(callNode) || !IsUsable(callNode) || HasCallToReturnSuccessor(callNode)) {
            return;
        }
        if (IsCalleeKnownToReturn(callNode, out SegmentedAddress? callee)) {
            EnqueueContinuation(callNode, worklist, InstructionSuccessorType.CallToReturn, false, ExploreSource.CallContinuation);
        } else if (callee is not null) {
            AddPendingCallSite(callee.Value, callNode);
        }
    }

    private bool IsCalleeKnownToReturn(CfgInstruction callNode, out SegmentedAddress? callee) {
        callee = CalleeEntry(callNode);
        if (callee is null) {
            return false;
        }
        if (TryGetInterruptVector(callNode, out byte vector)
            && _installedInterruptHandlers.IsInstalledHandler(callee.Value)) {
            // An int that still targets an emulator-installed handler returns, except the
            // DOS termination services.
            if (vector is 0x20 or 0x27) {
                return false;
            }
            return vector != 0x21 || !IsDosExitMovPredecessor(callNode);
        }
        return _calleeReturnEvidence.IsKnownToReturn(callee.Value);
    }

    /// <summary>
    /// True when the single predecessor of <paramref name="node"/> inside its own block is a
    /// <c>mov ah, imm8</c> or <c>mov ax, imm16</c> loading a DOS exit function (00h, 31h, 4Ch) into
    /// AH, the canonical setup of the terminating int 21h calls.
    /// </summary>
    private static bool IsDosExitMovPredecessor(CfgInstruction node) {
        CfgInstruction? previous = null;
        foreach (ICfgNode predecessor in node.Predecessors) {
            if (predecessor is CfgInstruction instruction && instruction.ContainingBlock == node.ContainingBlock) {
                if (previous is not null) {
                    return false;
                }
                previous = instruction;
            }
        }
        if (previous is null) {
            return false;
        }
        if (previous.ExecutionAst is not BlockNode block || block.Statements.Count == 0
            || block.Statements[0] is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode destination
            } assign) {
            return false;
        }
        ulong? immediate = assign.Right switch {
            ConstantNode constant => constant.Value,
            InstructionFieldNode field => field.ConstantValue,
            _ => null
        };
        if (immediate is null) {
            return false;
        }
        // The byte the mov loads into AH: the immediate itself for mov ah,imm8, the high byte for mov ax,imm16.
        ulong? ah = destination switch {
            { DataType.BitWidth: BitWidth.BYTE_8, RegisterIndex: 4 } => immediate.Value,
            { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: 0 } => immediate.Value >> 8,
            _ => null
        };
        return ah is 0x00 or 0x31 or 0x4C;
    }

    // A call site is usable while it is a live observed instruction or still a speculative
    // node in the index (a speculative instruction is never IsLive; pruning removes it from the
    // index). Dead observed nodes fail both tests.
    private static bool IsUsable(CfgInstruction node) => node.IsLive || node.IsSpeculative;

    private static bool HasCallToReturnSuccessor(CfgInstruction callNode) =>
        callNode.SuccessorsPerType.TryGetValue(InstructionSuccessorType.CallToReturn, out ISet<ICfgNode>? targets)
            && targets.Count > 0;

    private void AddPendingCallSite(SegmentedAddress callee, CfgInstruction callNode) {
        if (!_pendingCallSites.TryGetValue(callee, out List<CfgInstruction>? sites)) {
            sites = _pendingCallSites[callee] = [];
        }
        if (!sites.Contains(callNode)) {
            sites.Add(callNode);
        }
    }

    /// <summary>
    /// The callee just became known to return: wire the continuation of every pending call site
    /// still in the index. Sites pruned or already holding a CallToReturn successor are skipped.
    /// </summary>
    private void DrainPendingCallSites(SegmentedAddress callee) {
        if (!_pendingCallSites.Remove(callee, out List<CfgInstruction>? sites)) {
            return;
        }
        Queue<ExploreItem> worklist = new();
        foreach (CfgInstruction site in sites) {
            if (!_nodeIndex.Contains(site) || !IsUsable(site) || HasCallToReturnSuccessor(site)) {
                continue;
            }
            EnqueueContinuation(site, worklist, InstructionSuccessorType.CallToReturn, false, ExploreSource.CallContinuation);
        }
        Drain(worklist);
    }

    /// <summary>
    /// Enqueues the call/int continuation address (NextInMemoryAddress32) as an item of the given
    /// edge type and source. The continuation address is computed from instruction length, never
    /// by decoding the operand.
    /// </summary>
    private void EnqueueContinuation(CfgInstruction callNode, Queue<ExploreItem> worklist, InstructionSuccessorType edgeType, bool followContinuations, ExploreSource source) {
        SegmentedAddress continuation = callNode.NextInMemoryAddress32.ToSegmentedAddress();
        if (_nodeIndex.PoisonSet.Contains(continuation)) {
            return;
        }
        worklist.Enqueue(new ExploreItem(continuation, callNode, edgeType, followContinuations, source, null));
    }

    private void EnqueueStaticSuccessors(CfgInstruction instruction, Queue<ExploreItem> worklist, bool followContinuations) {
        foreach (SegmentedAddress successor in instruction.StaticSuccessorAddresses.Where(address => !_nodeIndex.PoisonSet.Contains(address))) {
            worklist.Enqueue(new ExploreItem(successor, instruction, InstructionSuccessorType.Normal, followContinuations, ExploreSource.Static, null));
        }
    }
}
