namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

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

/// <summary>
/// Answers "which registers may this call change": scans the already-decoded static subgraph of
/// a callee entry (observed and speculative nodes alike) and unions the
/// <see cref="RegisterWriteSet"/> of every instruction on every path. Nested calls contribute the
/// callee's own mask when it resolves; an <c>int</c> contributes the writes of the installed
/// emulator handler's service contract when the vector still belongs to the emulator and AH
/// selects a known service. An indirect call inside the callee unions the scans of its observed
/// (non-speculative) targets; an indirect jump is followed into its observed targets, whose
/// returns count as the callee's own. Anything the scan cannot account for - an undecoded
/// address, an invalid node, a far jump, an indirect site with no observed or unscannable
/// target, a leaf with no static successor, an instruction whose
/// write set is not understood, a nested call to a callee still being scanned, or a subgraph over
/// <see cref="CalleeReturnScanner.MaxNodes"/> nodes - makes the whole result unknown
/// (<see cref="RegisterBits.AllExceptCs"/>).
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
    /// saves and restores, whether one of the callee's own returns pops an immediate, and
    /// every address the scan visited (including nested callees), used for invalidation.</summary>
    private sealed record CachedScan(ushort Mask, ushort Saved, bool PopsImmediate,
        HashSet<SegmentedAddress> Addresses);

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
    private readonly Dictionary<SegmentedAddress, CachedScan> _cache = new();
    private readonly Dictionary<SegmentedAddress, List<SegmentedAddress>> _cachedEntriesByAddress = new();

    public CalleeWriteScanner(CfgNodeIndex nodeIndex, InstalledInterruptHandlers installedHandlers,
        InterruptVectorTable interruptVectorTable, Func<SegmentedAddress, bool> returnsBalanced) {
        _nodeIndex = nodeIndex;
        _installedHandlers = installedHandlers;
        _interruptVectorTable = interruptVectorTable;
        _returnsBalanced = returnsBalanced;
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
    /// nested scan depends on (the caller's own invalidation must cover them), and whether the
    /// result may be cached (false when it went through an <c>int</c>).</summary>
    private sealed record NestedResult(ushort Mask, bool Cacheable, HashSet<SegmentedAddress> Addresses) {
        public static NestedResult UnknownResult { get; } = new(Unknown, false, []);
    }

    private NestedResult MayWrite(SegmentedAddress calleeEntry, ScanContext context) {
        if (_cache.TryGetValue(calleeEntry, out CachedScan? cached)) {
            return new NestedResult(ApplyBalance(calleeEntry, cached), true, cached.Addresses);
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
        cached = new CachedScan(outcome.Mask, outcome.Saved, outcome.PopsImmediate, outcome.Addresses);
        if (outcome.Cacheable && !context.ContextDependent) {
            Store(calleeEntry, cached);
        }
        return new NestedResult(ApplyBalance(calleeEntry, cached), outcome.Cacheable,
            outcome.Addresses);
    }

    private NestedResult MayWriteAcross(CfgInstruction callNode, ScanContext context) {
        if (InterruptCallFacts.TryGetInterruptVector(callNode, out byte vector)) {
            // An int resolution depends on the IVT and on AH: never cached.
            return new NestedResult(InterruptWrites(callNode, vector), false, []);
        }
        if (callNode.StaticSuccessorAddresses.Count == 1) {
            return MayWrite(callNode.StaticSuccessorAddresses[0], context);
        }
        return NestedResult.UnknownResult;
    }

    /// <summary>The raw mask and saved registers a scan computes: SP and Saved kept, since only
    /// the query side knows whether the callee's return is balanced.</summary>
    private sealed record ScanOutcome(bool Succeeded, bool Cacheable, ushort Mask, ushort Saved,
        bool PopsImmediate, HashSet<SegmentedAddress> Addresses) {
        public static ScanOutcome Unknown { get; } = new(false, false, 0, 0, false, []);
    }

    private ScanOutcome Scan(SegmentedAddress calleeEntry, ScanContext context) {
        Queue<CfgInstruction> work = new(_nodeIndex.GetAtAddress(calleeEntry));
        if (work.Count == 0) {
            return ScanOutcome.Unknown;
        }
        HashSet<CfgInstruction> visited = [];
        HashSet<SegmentedAddress> addresses = [calleeEntry];
        Dictionary<CfgInstruction, ushort> acrossMasks = [];
        ushort mask = 0;
        bool popsImmediate = false;
        bool cacheable = true;
        while (work.Count > 0) {
            CfgInstruction node = work.Dequeue();
            if (!visited.Add(node)) {
                continue;
            }
            if (visited.Count > CalleeReturnScanner.MaxNodes) {
                return ScanOutcome.Unknown;
            }
            addresses.Add(node.Address);
            if (node.IsInvalid || node.ExecutionAst is null) {
                return ScanOutcome.Unknown;
            }
            IVisitableAstNode last = node.ExecutionAst is BlockNode block && block.Statements.Count > 0
                ? block.Statements[^1]
                : node.ExecutionAst;
            if (node.IsReturn) {
                if (last is not (ReturnNearNode or ReturnFarNode or ReturnInterruptNode)) {
                    return ScanOutcome.Unknown;
                }
                mask |= (ushort)RegisterBits.Sp;
                popsImmediate |= last switch {
                    ReturnNearNode ret => PopsImmediate(ret.BytesToPop),
                    ReturnFarNode ret => PopsImmediate(ret.BytesToPop),
                    _ => false
                };
                continue;
            }
            if (node.IsCall) {
                NestedResult across = AcrossInScan(node, context);
                if (across.Mask == Unknown) {
                    return ScanOutcome.Unknown;
                }
                acrossMasks[node] = across.Mask;
                mask |= across.Mask;
                cacheable &= across.Cacheable;
                addresses.UnionWith(across.Addresses);
                // The call's own frame is balanced by construction; execution continues at the
                // instruction after it, which must be decoded as well.
                if (!EnqueueAll(work, node.NextInMemoryAddress32.ToSegmentedAddress())) {
                    return ScanOutcome.Unknown;
                }
                continue;
            }
            if (node.IsJump) {
                if (last is JumpFarNode) {
                    return ScanOutcome.Unknown;
                }
                if (node.StaticSuccessorAddresses.Count == 0) {
                    // An indirect jump stays inside the callee: the observed targets' own
                    // returns count as this callee's returns.
                    if (!EnqueueObservedTargets(work, node)) {
                        return ScanOutcome.Unknown;
                    }
                    continue;
                }
                if (!EnqueueSuccessors(work, node)) {
                    return ScanOutcome.Unknown;
                }
                continue;
            }
            if (node.StaticSuccessorAddresses.Count == 0) {
                return ScanOutcome.Unknown;
            }
            ushort nodeMask = RegisterWriteSet.MayWrite(node);
            if (nodeMask == Unknown) {
                return ScanOutcome.Unknown;
            }
            mask |= nodeMask;
            if (!EnqueueSuccessors(work, node)) {
                return ScanOutcome.Unknown;
            }
        }
        ushort saved = CalleeSavedRegisters.Compute(calleeEntry, _nodeIndex, visited, acrossMasks);
        return new ScanOutcome(true, cacheable, mask, saved, popsImmediate, addresses);
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
        foreach (SegmentedAddress target in targets) {
            NestedResult result = MayWrite(target, context);
            if (result.Mask == Unknown) {
                return NestedResult.UnknownResult;
            }
            mask |= result.Mask;
            cacheable &= result.Cacheable;
            addresses.UnionWith(result.Addresses);
        }
        return new NestedResult(mask, cacheable, addresses);
    }

    private bool EnqueueObservedTargets(Queue<CfgInstruction> work, CfgInstruction node) {
        HashSet<SegmentedAddress> targets = ObservedTargets(node);
        if (targets.Count == 0 || targets.Count > SpeculativeExplorer.MaxCandidatesPerSite) {
            return false;
        }
        foreach (SegmentedAddress target in targets) {
            if (!EnqueueAll(work, target)) {
                return false;
            }
        }
        return true;
    }

    private bool EnqueueSuccessors(Queue<CfgInstruction> work, CfgInstruction node) {
        foreach (SegmentedAddress successor in node.StaticSuccessorAddresses) {
            if (!EnqueueAll(work, successor)) {
                return false;
            }
        }
        return true;
    }

    private bool EnqueueAll(Queue<CfgInstruction> work, SegmentedAddress address) {
        bool found = false;
        foreach (CfgInstruction target in _nodeIndex.GetAtAddress(address)) {
            found = true;
            work.Enqueue(target);
        }
        return found;
    }

    private static bool PopsImmediate(IVisitableAstNode bytesToPop) => bytesToPop switch {
        ConstantNode constant => constant.Value != 0,
        InstructionFieldNode field => field.ConstantValue != 0,
        _ => true
    };

    /// <summary>
    /// The writes the service behind <paramref name="vector"/> declares, when the IVT entry still
    /// belongs to the emulator and the service resolves to a known contract: a single-service
    /// handler, or an AH-selected service whose value the in-block predecessor loads. Unknown
    /// otherwise (a program handler, an undescribed service, an AH the dispatch cannot read).
    /// </summary>
    private ushort InterruptWrites(CfgInstruction intNode, byte vector) {
        if (!_installedHandlers.TryGetInstalledAddress(vector, out SegmentedAddress installed)
            || installed != _interruptVectorTable[vector]
            || !_installedHandlers.Handlers.TryGetValue(vector, out IInterruptHandler? handler)) {
            return Unknown;
        }
        InterruptServiceEntry? entry = handler.Services switch {
            HandlerServices.SingleService single => single.Service,
            HandlerServices.SelectedServices { Selector: ServiceSelector.Ah } selected
                when InterruptCallFacts.AhLoadedBefore(intNode) is { } ah =>
                    selected.Services.GetValueOrDefault(ah),
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
            if (!_cache.Remove(entry, out CachedScan? scan)) {
                continue;
            }
            foreach (SegmentedAddress visitedAddress in scan.Addresses) {
                if (_cachedEntriesByAddress.TryGetValue(visitedAddress, out List<SegmentedAddress>? entries)
                    && entries.Remove(entry) && entries.Count == 0) {
                    _cachedEntriesByAddress.Remove(visitedAddress);
                }
            }
        }
    }
}
