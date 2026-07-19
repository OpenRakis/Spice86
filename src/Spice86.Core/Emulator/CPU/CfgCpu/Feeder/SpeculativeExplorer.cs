namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
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
    /// Per-worklist-item data carrying the target address, predecessor, edge type, and trust flag.
    /// Trust (<see cref="FollowContinuations"/>) propagates along intra-routine successors (Normal edges)
    /// and also into callees. Only trusted items eagerly wire <see cref="InstructionSuccessorType.CallToReturn"/>
    /// continuation edges past call/int instructions.
    /// </summary>
    private readonly record struct ExploreItem(
        SegmentedAddress Target,
        CfgInstruction? Predecessor,
        InstructionSuccessorType EdgeType,
        bool FollowContinuations);

    private readonly InstructionParser _instructionParser;
    private readonly CfgNodeIndex _nodeIndex;
    private readonly NodeLinker _nodeLinker;

    public SpeculativeExplorer(InstructionParser instructionParser, CfgNodeIndex nodeIndex, NodeLinker nodeLinker) {
        _instructionParser = instructionParser;
        _nodeIndex = nodeIndex;
        _nodeLinker = nodeLinker;
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
        worklist.Enqueue(new ExploreItem(entry, null, InstructionSuccessorType.Normal, FollowContinuations: true));
        Drain(worklist);
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
                _nodeIndex.PoisonSet.Add(address);
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
            candidate.SetSpeculative(true);
            _nodeIndex.Insert(candidate);
            if (item.Predecessor is not null) {
                _nodeLinker.Link(item.EdgeType, item.Predecessor, candidate);
            }
            EnqueueSuccessorsForNode(candidate, worklist, item.FollowContinuations);
        }
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
                worklist.Enqueue(new ExploreItem(successor, node, InstructionSuccessorType.Normal, FollowContinuations: followContinuations));
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
        if (followContinuations && !node.IsReturn && !node.IsJump && !node.IsInvalid) {
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
        worklist.Enqueue(new ExploreItem(continuation, callNode, edgeType, followContinuations));
    }

    private void EnqueueStaticSuccessors(CfgInstruction instruction, Queue<ExploreItem> worklist, bool followContinuations) {
        foreach (SegmentedAddress successor in instruction.StaticSuccessorAddresses.Where(address => !_nodeIndex.PoisonSet.Contains(address))) {
            worklist.Enqueue(new ExploreItem(successor, instruction, InstructionSuccessorType.Normal, followContinuations));
        }
    }
}
