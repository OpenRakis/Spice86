namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// The per-method-entry register conventions of a generated program, computed by one program-wide
/// fixed point over the post-pipeline SSA of every method: <see cref="EffectOf"/> (its Reads: the
/// register bytes and flags an entry reads before writing them; its Writes: the bytes a return may
/// leave different from what the entry got) and <see cref="OutputsOf"/> (the changes a caller
/// actually reads after the call). Printed as a doc comment above each generated method.
/// <para>All three sets only grow from empty over a fixed set of bits, so the least fixed point is
/// reached in finitely many iterations; it is sound for finite call trees, which covers recursion:
/// every rule over-approximates, so an execution the sets describe is a superset of the real one.
/// The rules:</para>
/// <para>- <c>Needed(version)</c>, the bytes of a version later code may still observe, is the union
/// over its uses: a phi operand needs its result's bytes; a call or <c>int</c> needs the bytes its
/// resolved targets input plus the bytes still needed after the call that no target changes (an
/// unresolved call needs everything; an <c>int</c> also needs the six status flags and DF, which it
/// pushes); a push feeding only complete pop-assigns of the same register
/// needs what each popped version needs (other pushes make the value escape into memory); an event
/// check only forwards through the version it defines (the hardware handler's own reads are not the
/// method's inputs); a partial definition forwards the bytes it does not write; a return exit needs
/// the bytes the method's entries output; an abort exit (a <c>FailAsUntested</c>, a
/// <see cref="ThrowNode"/>, or a return that leaves generated code like <c>Hlt</c>) needs only the
/// bytes its transfer literally references - a terminal hlt observes memory only; a
/// resolved exit to another partition needs the targets' inputs (unresolved: everything); anything
/// else needs its effect reads.</para>
/// <para>- <c>EffectOf(E).Reads</c> is the union of <c>Needed</c> over the values' versions at E's entry
/// block. Call-target bytes are added before the flow-on bytes, so a value the caller sets just for
/// the callee is not an input of the caller.</para>
/// <para>- <c>EffectOf(E).Writes</c> is the union over the method's exits of the bytes whose exit version
/// is not preserved (still provably E's entry value), computed by <see cref="IrPreservation"/>: the
/// entry version preserves all bytes, a phi the operand intersection, a pop matched to pushes of
/// preserved bytes restores them, a call keeps the previous bytes its targets do not change, an
/// event check keeps the previous bytes its own effect does not write, another partial definition
/// keeps the bytes it does not write and a full definition nothing. An
/// <c>iret</c> exit also changes the six status flags and DF (a handler can patch the stacked FLAGS
/// image); a resolved exit to another partition adds the targets' changes; an unresolved non-return,
/// non-abort exit changes everything; an abort exit changes nothing.</para>
/// <para>- <c>OutputsOf(E)</c> is <c>EffectOf(E).Writes</c> intersected with what is live after E.
/// When E's only ways in are calls, that is the union over the call sites targeting E of the bytes
/// needed after each call. A non-call way in (<see cref="CfgCodePartitionTransferKind.CallOut"/>
/// excepted) narrows the same way: E's method still leaves only through its return exits, so what
/// is read after E is the union of those exits' landing demands - the inputs of the entries the
/// returns land on plus the live-after-call of every call site whose continuation is a landing.
/// It widens back to every reported value for an execution context entry, when a return's landing
/// is unresolved (a machine-leaving way out the model does not describe), or when the method has a
/// transfer exit; the program entry keeps the terminal-observation rule instead.</para>
/// <para>- The live-after-call of a call site is itself fixed-point state: it is the union over
/// values of <c>Needed</c> of the version right after the call - the call's defined version when it
/// has one, else the version that reaches the call - restricted to uses that can run after the
/// call (the reaching version's use at the call itself and uses before it do not count, and a
/// version is not demanded just because it was needed earlier; a block a cycle can re-enter counts
/// its earlier uses since they run again). A <c>ret</c> that lands on a call site's continuation
/// node reads that set instead of widening to <c>Reportable</c>.</para>
/// </summary>
internal sealed class IrCallingConventions {
    /// <summary>The values the sets report: the general register bytes except ESP, the segment
    /// registers except CS and SS, the six status flags and DF.</summary>
    internal static readonly CpuValues Reportable =
        CpuValues.Eax | CpuValues.Ecx | CpuValues.Edx | CpuValues.Ebx
        | CpuValues.Ebp | CpuValues.Esi | CpuValues.Edi
        | CpuValues.Es | CpuValues.Ds | CpuValues.Fs | CpuValues.Gs
        | CpuValues.StatusFlags | CpuValues.Direction;

    private const int ValueCount = (int)IrSsaValue.Direction + 1;
    private const int MaxIterations = 500;

    private readonly CfgGeneratorContext _context;
    private readonly Dictionary<ICfgNode, CfgCodePartitionEntry> _entryByNode = new();
    private readonly Dictionary<SegmentedAddress, CfgCodePartitionEntry> _entryByAddress = new();
    private readonly Dictionary<CfgCodePartitionEntry, EntryState> _states = new();
    private readonly Dictionary<string, MethodData> _methodByName = new();
    private readonly Dictionary<CfgCodePartitionEntry, List<(MethodData Caller, CallSite Site)>> _callersOf = new();
    private readonly Dictionary<CfgCodePartitionEntry, List<(MethodData Caller, ExitSite Site)>> _transfersTo = new();
    private readonly Dictionary<ICfgNode, List<(MethodData Caller, CallSite Site)>> _callSitesByContinuation = new();
    private readonly Dictionary<CfgCodePartitionEntry, MethodData> _methodByEntry = new();
    private readonly Dictionary<CfgCodePartitionEntry, HashSet<MethodData>> _readersOf = new();
    private readonly List<MethodData> _methods = [];
    private readonly bool _observed;
    private readonly HardwareInterruptEffectBuilder _effectBuilder;
    private readonly Dictionary<string, CalleeReturnEffect> _provedReturns = new(
        StringComparer.Ordinal);

    private IrCallingConventions(CfgGeneratorContext context, bool observed,
        HardwareInterruptEffectBuilder effectBuilder) {
        _context = context;
        _observed = observed;
        _effectBuilder = effectBuilder;
        foreach (CfgCodePartition partition in context.Program.Partitions) {
            foreach (CfgCodePartitionEntry entry in partition.Entries) {
                _entryByNode[entry.Node] = entry;
                _entryByAddress[entry.Address] = entry;
                _states[entry] = new EntryState();
            }
        }
    }

    /// <summary>How many iterations the last <see cref="Compute"/> ran to the fixed point.</summary>
    internal int Iterations { get; private set; }

    /// <summary>Computes the conventions of <paramref name="methods"/> to a fixed point, invoking
    /// <paramref name="roundStarted"/> with the 1-based number of each fixed-point iteration.
    /// <paramref name="observed"/> adds the terminal-observation demand: an unresolved landing of a
    /// call exit is then read as the whole reportable state, and that demand propagates through the
    /// same rules.
    /// The observed sets drive only the read sets and write-backs of round-two exits; the code
    /// demand (what generated code reads after a call) drives the printed conventions and the
    /// typed signatures.</summary>
    public static IrCallingConventions Compute(IReadOnlyList<PreparedMethod> methods,
        CfgGeneratorContext context, bool observed, Action<int> roundStarted,
        HardwareInterruptEffectBuilder effectBuilder) {
        IrCallingConventions conventions = new(context, observed, effectBuilder);
        conventions.Preprocess(methods);
        conventions.SeedUnknownWays();
        conventions.Iterate(roundStarted);
        conventions.ProveReturnsToCaller();
        return conventions;
    }

    /// <summary>The register bytes and flags <paramref name="entry"/> reads before writing them and
    /// the ones a return of it may leave changed.</summary>
    public CallEffect EffectOf(CfgCodePartitionEntry entry) =>
        _states.TryGetValue(entry, out EntryState? state)
            ? new(state.Inputs, state.Changes)
            : new(Reportable, Reportable);

    /// <summary>The changes of <paramref name="entry"/> its callers read after the call.</summary>
    public CpuValues OutputsOf(CfgCodePartitionEntry entry) =>
        _states.TryGetValue(entry, out EntryState? state) ? state.Outputs : Reportable;

    /// <summary>The bytes <paramref name="entry"/> may read before writing them, under this
    /// conventions instance's demand; every reportable value when the entry is not modelled.</summary>
    internal CpuValues InputsOf(CfgCodePartitionEntry entry) =>
        _states.TryGetValue(entry, out EntryState? state) ? state.Inputs : Reportable;

    /// <summary>Whether <paramref name="exit"/> of <paramref name="methodName"/> is a method
    /// return - not an abort and not a transfer to another partition.</summary>
    internal bool IsReturnExit(string methodName, IrExit exit) =>
        _methodByName.TryGetValue(methodName, out MethodData? data)
        && data.ExitByTerminator.TryGetValue(exit, out ExitSite? site)
        && site.Kind == ExitKind.Return;

    /// <summary>The bytes the observed landings of the return <paramref name="exit"/> of
    /// <paramref name="methodName"/> read at their entries - see <see cref="LandingInputs"/>.
    /// Every reportable value when the exit is not a modelled return.</summary>
    internal CpuValues LandingInputsOf(string methodName, IrExit exit) =>
        _methodByName.TryGetValue(methodName, out MethodData? data)
        && data.ExitByTerminator.TryGetValue(exit, out ExitSite? site)
        && site.Kind == ExitKind.Return
            ? LandingInputs(site)
            : Reportable;

    /// <summary>Whether <paramref name="exit"/> of <paramref name="methodName"/> transfers to
    /// another generated partition whose targets are all resolved.</summary>
    internal bool IsResolvedTransferExit(string methodName, IrExit exit) =>
        _methodByName.TryGetValue(methodName, out MethodData? data)
        && data.ExitByTerminator.TryGetValue(exit, out ExitSite? site)
        && site.Kind == ExitKind.TransferResolved;

    /// <summary>The bytes the transfer <paramref name="exit"/> of <paramref name="methodName"/>
    /// must deliver - see <see cref="TransferExitReads"/>: the targets' entry inputs plus the
    /// landings of the targets' own returns, minus the bytes each target changes. Every
    /// reportable value when the exit is not a modelled resolved transfer.</summary>
    internal CpuValues TransferInputsOf(string methodName, IrExit exit) {
        if (!_methodByName.TryGetValue(methodName, out MethodData? data)
                || !data.ExitByTerminator.TryGetValue(exit, out ExitSite? site)
                || site.Kind != ExitKind.TransferResolved) {
            return Reportable;
        }
        return TransferExitReads(site);
    }

    /// <summary>The bytes a resolved transfer exit must deliver: the union of its targets' entry
    /// inputs plus, per target, the inputs of the entries the target's own returns can land on,
    /// minus the bytes that target changes - a pushed-return continuation such as
    /// <c>push 0x0049; jmp f</c> keeps reading the caller's state through the callee, so a byte
    /// the callee does not touch must still arrive at the landing. <see cref="LandingInputs"/>
    /// already widens an unmodelled landing to every reportable value.</summary>
    private CpuValues TransferExitReads(ExitSite site) {
        CpuValues inputs = CpuValues.None;
        foreach (CfgCodePartitionEntry target in site.Targets) {
            inputs |= _states[target].Inputs | ObservedEntryReads(target);
            if (!_methodByEntry.TryGetValue(target, out MethodData? targetData)) {
                continue;
            }
            CpuValues targetChanges = _states[target].Changes;
            foreach (ExitSite targetExit in targetData.Exits
                    .Where(targetExit => targetExit.Kind == ExitKind.Return)) {
                inputs |= LandingInputs(targetExit) & ~targetChanges;
            }
        }
        return inputs;
    }

    /// <summary>The bytes observers at <paramref name="entry"/> read of the versions that arrive
    /// at the entry before any write - a hardware interrupt handler delivered at the entry's
    /// event checks reads machine registers under the charged contract. Observed demand only:
    /// under the code conventions this is <see cref="CpuValues.None"/> so the printed inputs stay
    /// code demand; under the observed conventions it widens what a transfer or a return landing
    /// on the entry must deliver.</summary>
    private CpuValues ObservedEntryReads(CfgCodePartitionEntry entry) {
        if (!_observed || !_methodByEntry.TryGetValue(entry, out MethodData? target)) {
            return CpuValues.None;
        }
        CpuValues reads = CpuValues.None;
        foreach (EntrySite site in target.Entries) {
            if (!ReferenceEquals(site.Entry, entry)) {
                continue;
            }
            for (int v = 0; v < ValueCount; v++) {
                int version = site.EntryVersions[v];
                if (version < 0) {
                    continue;
                }
                foreach (IrSsaUse use in target.Ssa.Uses(version)) {
                    if (use.Statement?.Node is CheckExternalEventsNode) {
                        reads |= IrSsaValues.MaskOf((IrSsaValue)v);
                        break;
                    }
                }
            }
        }
        return reads;
    }


    /// <summary>Whether the method of <paramref name="entry"/> has a return exit or a resolved
    /// exit to another partition.</summary>
    public bool Returns(CfgCodePartitionEntry entry) =>
        _states.TryGetValue(entry, out EntryState? state) && state.Returns;

    /// <summary>Whether <paramref name="statement"/> of <paramref name="methodName"/> is a resolved
    /// call site whose every target returns without changing a byte of
    /// <paramref name="mask"/>.</summary>
    public bool CallPreserves(string methodName, IrStatement statement, CpuValues mask) {
        if (!_methodByName.TryGetValue(methodName, out MethodData? data)
            || !data.CallByStatement.TryGetValue(statement, out CallSite? call)
            || !call.Resolved) {
            return false;
        }
        foreach (CfgCodePartitionEntry target in call.Targets) {
            if (!Returns(target) || (EffectOf(target).Writes & mask) != CpuValues.None) {
                return false;
            }
        }
        return true;
    }

    /// <summary>Why a call site did not get a summary: it is not a modelled call
    /// (<see cref="CallSummarySkip.NotCallSite"/>), its target or continuation is not fully
    /// observed (<see cref="CallSummarySkip.Unresolved"/>), a method reachable from its target
    /// through calls has an observed fault edge (<see cref="CallSummarySkip.FaultReachable"/>) or
    /// its target never returns (<see cref="CallSummarySkip.NonReturning"/>).</summary>
    internal enum CallSummarySkip { NotCallSite, Unresolved, FaultReachable, NonReturning }

    /// <summary>The effect a resolved call statement gets in round two: the union of its target's
    /// inputs and changes (under this conventions instance) plus the stack reads and the hardware
    /// contract's <paramref name="contract"/> reads and writes - the callee's event checks can
    /// deliver hardware interrupts. Both tokens are read and written and interrupts delivered, as
    /// any call. <see langword="null"/> - the call keeps <see cref="IrEffects.Unknown"/> - when the
    /// site is not a modelled call, the single target form is not observed, no return at all was
    /// observed (neither the normal continuation nor a redirected landing - the callee's effects
    /// are the same either way), the target never returns, or a fault edge is reachable from it:
    /// a fault handler observes every register at the fault point, which no summary
    /// covers.</summary>
    internal (IrEffects? Effect, CallSummarySkip Skip) CallSummaryOf(
        string methodName, IrStatement statement, CallEffect contract) =>
        CallSummaryOf(CallSiteOf(methodName, statement), statement.Effects, contract);

    internal (IrEffects? Effect, CallSummarySkip Skip) CallSummaryOf(
        string methodName, IrTerminator terminator, CallEffect contract) =>
        CallSummaryOf(CallSiteOf(methodName, terminator), terminator.Effects, contract);

    private CallSite? CallSiteOf(string methodName, IrStatement statement) =>
        _methodByName.TryGetValue(methodName, out MethodData? data)
            && data.CallByStatement.TryGetValue(statement, out CallSite? call)
                ? call
                : null;

    private CallSite? CallSiteOf(string methodName, IrTerminator terminator) =>
        _methodByName.TryGetValue(methodName, out MethodData? data)
        && data.CallByTerminator.TryGetValue(terminator, out CallSite? call)
            ? call
            : null;

    private (IrEffects? Effect, CallSummarySkip Skip) CallSummaryOf(
        CallSite? call, IrEffects siteEffects, CallEffect contract) {
        if (call is null) {
            return (null, CallSummarySkip.NotCallSite);
        }
        if (!call.Resolved || call.Targets.Count != 1
            || (!call.ObservedContinuation && !call.RedirectedReturn)) {
            return (null, CallSummarySkip.Unresolved);
        }
        CfgCodePartitionEntry target = call.Targets[0];
        if (!Returns(target)) {
            return (null, CallSummarySkip.NonReturning);
        }
        if (ReachesFaultEdge(target)) {
            return (null, CallSummarySkip.FaultReachable);
        }
        EntryState state = _states[target];
        IrEffects effect = new(
            state.Inputs | ObservedEntryReads(target) | CpuValues.Sp | CpuValues.Ss | CpuValues.Cs
                | contract.Reads | PushedBytesReachable(target),
            CpuValues.None, state.Changes | CpuValues.Sp | contract.Writes,
            IrTokens.All, IrTokens.All, siteEffects.MayFault, true);
        return (effect, CallSummarySkip.NotCallSite);
    }

    /// <summary>The bytes the resolved targets of <paramref name="statement"/>'s call site input;
    /// <see cref="Reportable"/> when the site is not a modelled call.</summary>
    internal CpuValues CallTargetsInputsOf(string methodName, IrStatement statement) =>
        _methodByName.TryGetValue(methodName, out MethodData? data)
        && data.CallByStatement.TryGetValue(statement, out CallSite? call)
        && call.Resolved
            ? TargetsInputs(call)
            : Reportable;

    internal CpuValues CallTargetsInputsOf(string methodName, IrTerminator terminator) =>
        _methodByName.TryGetValue(methodName, out MethodData? data)
        && data.CallByTerminator.TryGetValue(terminator, out CallSite? call)
        && call.Resolved
            ? TargetsInputs(call)
            : Reportable;

    /// <summary>Whether a method with an observed fault edge - a statement of an instruction that
    /// can fault into a handler - is reachable from <paramref name="entry"/>'s method through
    /// resolved calls and resolved partition transfers, transitively.</summary>
    private bool ReachesFaultEdge(CfgCodePartitionEntry entry) {
        if (!_methodByEntry.TryGetValue(entry, out MethodData? start)) {
            return true;
        }
        HashSet<MethodData> seen = [start];
        Queue<MethodData> work = new([start]);
        while (work.Count > 0) {
            MethodData method = work.Dequeue();
            if (method.HasFaultEdge || method.HasUnresolvedTransfer) {
                return true;
            }
            foreach (CallSite call in method.Calls.Where(call => call.Resolved)) {
                foreach (MethodData next in call.Targets
                        .Where(_methodByEntry.ContainsKey)
                        .Select(target => _methodByEntry[target])
                        .Where(seen.Add)) {
                    work.Enqueue(next);
                }
            }
            foreach (MethodData next in method.TransferTargets
                    .Where(_methodByEntry.ContainsKey)
                    .Select(target => _methodByEntry[target])
                    .Where(seen.Add)) {
                work.Enqueue(next);
            }
        }
        return false;
    }

    /// <summary>The register bytes a method reachable from <paramref name="entry"/> through
    /// resolved calls and resolved partition transfers pushes onto the stack. A pushed value
    /// lands in RAM and stays observable in the final machine state even when a complete
    /// pop-restore reads the slot back - the convention's save/restore narrowing correctly
    /// reports the register preserved but says nothing about the bytes left in memory, so the
    /// caller's register must still hold the pushed version when the call runs.</summary>
    private CpuValues PushedBytesReachable(CfgCodePartitionEntry entry) {
        if (!_methodByEntry.TryGetValue(entry, out MethodData? start)) {
            return Reportable;
        }
        CpuValues pushed = CpuValues.None;
        HashSet<MethodData> seen = [start];
        Queue<MethodData> work = new([start]);
        while (work.Count > 0) {
            MethodData method = work.Dequeue();
            foreach (IrStatement statement in method.Source.IrMethod.Blocks
                     .SelectMany(block => block.Statements)) {
                foreach (StackCallNode node in IrAstChildren.Descendants(statement.Node)
                         .OfType<StackCallNode>()
                         .Where(node => node.Operation == StackOperation.Push
                             && node.Arguments is [RegisterNode])) {
                    pushed |= IrValueSets.Of((RegisterNode)node.Arguments[0]);
                }
            }
            foreach (CallSite call in method.Calls.Where(call => call.Resolved)) {
                foreach (MethodData next in call.Targets
                        .Where(_methodByEntry.ContainsKey)
                        .Select(target => _methodByEntry[target])
                        .Where(seen.Add)) {
                    work.Enqueue(next);
                }
            }
            foreach (MethodData next in method.TransferTargets
                    .Where(_methodByEntry.ContainsKey)
                    .Select(target => _methodByEntry[target])
                    .Where(seen.Add)) {
                work.Enqueue(next);
            }
        }
        return pushed;
    }

    /// <summary>The stack slots the preprocess computed for <paramref name="methodName"/>;
    /// <see cref="IrStackSlots.Empty"/> when the method has no stack node.</summary>
    internal IrStackSlots SlotsOf(string methodName) {
        if (_methodByName.TryGetValue(methodName, out MethodData? data)) {
            return data.Slots;
        }
        return IrStackSlots.Empty;
    }

    /// <summary>The proved return shape of <paramref name="methodName"/>: the distance and the
    /// popped byte count of its <c>ret</c> when the method is proved to return to its caller, or
    /// <c>null</c> otherwise. The single owner of the "returns to its caller with <c>ret n</c>"
    /// fact: <see cref="TypedSignatures"/> gates typed signatures on it and
    /// <see cref="IrStackSlots"/> advances the offset by the pop count at a proved call.</summary>
    internal CalleeReturnEffect? ReturnsToCallerOf(string methodName) =>
        _provedReturns.TryGetValue(methodName, out CalleeReturnEffect? effect) ? effect : null;

    /// <summary>The fact a resolved call consumes: <paramref name="methodName"/> returns to its
    /// caller at <paramref name="distance"/> popping <paramref name="poppedBytes"/> bytes beyond
    /// the return address.</summary>
    internal sealed record CalleeReturnEffect(CallDistance Distance, BitWidth Width, int PoppedBytes);

    /// <summary>The least fixed point of decision 3: start with no callee proved, prove every
    /// method that structurally returns to its caller under the current slot map, recompute the
    /// slots of every method so proved callees advance their callers' offsets, and stop when no
    /// method newly proves. Recursive cycles stay unproved, which is sound: proving only adds
    /// information, so the map can only grow. Throws when it has not converged after 8 rounds.</summary>
    private void ProveReturnsToCaller() {
        for (int round = 0; ; round++) {
            bool changed = false;
            foreach (MethodData method in _methods) {
                string name = method.Source.Method.MethodName;
                if (!_provedReturns.ContainsKey(name) && TryProveReturn(method) is { } effect) {
                    _provedReturns[name] = effect;
                    changed = true;
                }
            }
            if (!changed) {
                return;
            }
            if (round >= 7) {
                throw new InvalidOperationException("Returns-to-caller proof did not converge.");
            }
            foreach (MethodData method in _methods) {
                RefreshSlots(method);
            }
        }
    }

    /// <summary>Recomputes <paramref name="method"/>'s slot map under the current proved set and
    /// the preservation derived from it.</summary>
    private void RefreshSlots(MethodData method) {
        IrStackSlots slots = IrStackSlots.Empty;
        if (IrStackSlots.NeedsStackSlots(method.Source.IrMethod)) {
            slots = IrStackSlots.Compute(method.Source.IrMethod,
                new IrMemoryAliasAnalysis(method.Source.IrMethod, method.Ssa, method.Source.EntryFacts, _context.Policy),
                statement => CallPopCount(method, statement), SharedEntryState(method));
        }
        method.Slots = slots;
        method.Preservation = CreatePreservation(method.Source, slots);
    }

    /// <summary>The entry state of a shared entry reached only by resolved partition transfers: a
    /// tail-merge method (the Watcom <c>mov sp,bp</c> epilogue shared by <c>jmp</c>) is entered with
    /// the SP and BP its jump sites hold, so its entry state adopts the jumpers' frame - the SP
    /// delta is the jumpers' uniform offset at the jump, the BP copy their uniform BP delta. For
    /// <c>jmp epi</c> callers that did <c>push x3; mov bp,sp</c> both are -6: the epilogue's
    /// <c>mov sp,bp</c> restores SP to the trusted copy and the pops land the <c>ret</c> on delta 0
    /// of the jumpers' frame - the stacked return address their callers pushed. The flat entry
    /// state when any way in disagrees or is unknown, when a call site also reaches the entry (its
    /// pushed return sits at the entry SP, not the jumpers' frame), or when the entry is not a
    /// shared one.</summary>
    private IrStackState SharedEntryState(MethodData method) {
        IReadOnlyList<CfgCodePartitionEntry> entries = method.Source.Method.Entries;
        if (entries.Count != 1
            || entries[0].Kind != CfgCodePartitionEntryKind.SharedEntry
            || _callersOf.ContainsKey(entries[0])
            || !_transfersTo.TryGetValue(entries[0],
                    out List<(MethodData Caller, ExitSite Site)>? sites)) {
            return IrStackState.Entry;
        }
        int? offset = null;
        int? bp = null;
        bool bpKnown = true;
        foreach ((MethodData caller, ExitSite site) in sites) {
            int? siteOffset = caller.Slots.EntryDelta(caller.Slots.OffsetAtBlockEnd(site.Block));
            if (siteOffset is null || (offset is not null && siteOffset != offset)) {
                return IrStackState.Entry;
            }
            offset = siteOffset;
            int? siteBp = caller.Slots.EntryDelta(caller.Slots.BpOffsetAtBlockEnd(site.Block));
            if (siteBp is null || (bp is not null && siteBp != bp)) {
                bpKnown = false;
            } else {
                bp = siteBp;
            }
        }
        return IrStackState.OfOffset(IrStackOffset.Known(null, offset!.Value))
            .WithBp(bpKnown ? IrStackOffset.Known(null, bp!.Value) : IrStackOffset.Unknown);
    }

    /// <summary>The pop count the call <paramref name="statement"/> of <paramref name="caller"/>
    /// leaves SP advanced by: a resolved call with exactly one observed target entry, an observed
    /// continuation and no redirected return, whose target method's only entry is that target and
    /// is proved to return to its caller with a <c>ret n</c> of the call's distance.
    /// <c>null</c> for any other statement.</summary>
    private int? CallPopCount(MethodData caller, IrStatement statement) {
        if (!caller.CallByStatement.TryGetValue(statement, out CallSite? call)
            || !call.Resolved || !call.ObservedContinuation || call.RedirectedReturn
            || call.Targets.Count != 1) {
            return null;
        }
        CfgCodePartitionEntry target = call.Targets[0];
        if (!_methodByEntry.TryGetValue(target, out MethodData? callee)
            || callee.Source.Method.Entries.Count != 1
            || !ReferenceEquals(callee.Source.Method.Entries[0], target)
            || !_provedReturns.TryGetValue(callee.Source.Method.MethodName,
                out CalleeReturnEffect? effect)
            || CallHelperOf(statement)?.Distance != effect.Distance
            || CallHelperOf(statement)?.Width != effect.Width) {
            return null;
        }
        return effect.PoppedBytes;
    }

    /// <summary>The call helper of a statement, or <c>null</c> when the statement is no call.</summary>
    private static ProcedureCallHelperNode? CallHelperOf(IrStatement statement) =>
        IrAstChildren.Descendants(statement.Node).OfType<ProcedureCallHelperNode>()
            .FirstOrDefault();

    /// <summary>Whether <paramref name="data"/>'s method structurally returns to its caller: the
    /// <see cref="TypedSignatures"/> return rule, moved here. Holds when every return exit reads
    /// the stacked return address the caller pushed - SP at the exit at entry delta 0, or a
    /// recognized standard epilogue restoring it - and no proved frame access writes the return
    /// frame. The effect it returns is the uniform distance and pop count of the method's return
    /// helpers; a method with no or mixed returns is not proved.</summary>
    private CalleeReturnEffect? TryProveReturn(MethodData data) {
        MethodPlan plan = data.Source.Method;
        if (plan.Entries.Count != 1) {
            return null;
        }
        IrMethod method = data.Source.IrMethod;
        CallDistance distance = CallDistance.Near;
        BitWidth width = BitWidth.WORD_16;
        int poppedBytes = 0;
        int returns = 0;
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is not IrExit exit) {
                continue;
            }
            foreach (ProcedureReturnHelperNode helper in IrAstChildren
                    .Descendants(exit.Transfer).OfType<ProcedureReturnHelperNode>()) {
                int popCount = PopCount(helper);
                if (helper.Width is not (BitWidth.WORD_16 or BitWidth.DWORD_32) || popCount < 0
                    || (returns > 0 && (helper.Distance != distance || helper.Width != width
                        || popCount != poppedBytes))) {
                    return null;
                }
                distance = helper.Distance;
                width = helper.Width;
                poppedBytes = popCount;
                returns++;
            }
        }
        if (returns == 0) {
            return null;
        }
        IrStackSlots slots = data.Slots;
        bool writesStack = method.Blocks.SelectMany(block => block.Statements).Any(statement =>
            ((statement.Effects.MustWrite | statement.Effects.MayWrite)
                & (CpuValues.Esp | CpuValues.Ss)) != CpuValues.None);
        bool untracked = ReferenceEquals(slots, IrStackSlots.Empty);
        if (untracked && writesStack) {
            return null;
        }
        IrFrames frames = IrFrames.Compute(method, data.Ssa, slots,
            statement => statement.Origin is CfgInstruction instruction
                ? _effectBuilder.CallTargetWrites(instruction)
                : CpuValues.Registers);
        int returnBytes = (distance, width) == (CallDistance.Near, BitWidth.WORD_16) ? 2
            : distance == CallDistance.Near ? 4
            : width == BitWidth.WORD_16 ? 4 : 8;
        if (frames.Accesses.Any(access =>
                access.IsWrite && access.EntryOffset >= 0 && access.EntryOffset < returnBytes)) {
            return null;
        }
        IrEntry irEntry = method.Entries
            .Single(methodEntry => methodEntry.Node.Equals(plan.Entries[0].Node));
        IrFrame? frame = frames.FrameAt(irEntry);
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is not IrExit exit || !IsReturnHelperExit(exit)) {
                continue;
            }
            if (untracked || frames.EntryDelta(slots.OffsetAtBlockEnd(block)) == 0) {
                continue;
            }
            if (frame?.Epilogues.Any(epilogue => epilogue.Exit == exit
                    && epilogue.Kind is IrFrameEpilogueKind.MoveSpPopBp
                        or IrFrameEpilogueKind.Leave) == true) {
                continue;
            }
            return null;
        }
        return new CalleeReturnEffect(distance, width, poppedBytes);
    }

    private static bool IsReturnHelperExit(IrExit exit) =>
        IrAstChildren.Descendants(exit.Transfer)
            .OfType<ProcedureReturnHelperNode>().Any();

    private static int PopCount(ProcedureReturnHelperNode helper) => helper.Arguments switch {
        [] => 0,
        [IVisitableAstNode argument] => PopCountArgument(argument),
        _ => -1
    };

    private static int PopCountArgument(IVisitableAstNode argument) {
        while (argument is ExplicitCastNode cast) {
            argument = cast.Value;
        }
        if (ConstantEvaluation.Resolved(argument) is not ConstantNode { Value: <= ushort.MaxValue } constant) {
            return -1;
        }
        return (int)constant.Value;
    }

    /// <summary>The names of a set, in report order (<c>AX, BX</c>, <c>flags</c> for the six status
    /// flags, <c>none</c>, <c>all</c>).</summary>
    internal static string Describe(CpuValues values) {
        if (values == CpuValues.None) {
            return "none";
        }
        if (values == Reportable) {
            return "all";
        }
        List<string> names = [];
        GeneralRegister(names, values, CpuValues.Eax, "EAX", "AX", "AL", "AH");
        GeneralRegister(names, values, CpuValues.Ecx, "ECX", "CX", "CL", "CH");
        GeneralRegister(names, values, CpuValues.Edx, "EDX", "DX", "DL", "DH");
        GeneralRegister(names, values, CpuValues.Ebx, "EBX", "BX", "BL", "BH");
        WordRegister(names, values, CpuValues.Ebp, "EBP", "BP");
        WordRegister(names, values, CpuValues.Esi, "ESI", "SI");
        WordRegister(names, values, CpuValues.Edi, "EDI", "DI");
        Flag(names, values, CpuValues.Es, "ES");
        Flag(names, values, CpuValues.Ds, "DS");
        Flag(names, values, CpuValues.Fs, "FS");
        Flag(names, values, CpuValues.Gs, "GS");
        if ((values & CpuValues.StatusFlags) == CpuValues.StatusFlags) {
            names.Add("flags");
        } else {
            Flag(names, values, CpuValues.Carry, "CF");
            Flag(names, values, CpuValues.Parity, "PF");
            Flag(names, values, CpuValues.Auxiliary, "AF");
            Flag(names, values, CpuValues.Zero, "ZF");
            Flag(names, values, CpuValues.Sign, "SF");
            Flag(names, values, CpuValues.Overflow, "OF");
        }
        Flag(names, values, CpuValues.Direction, "DF");
        return string.Join(", ", names);
    }

    private static void GeneralRegister(List<string> names, CpuValues values, CpuValues mask,
        string name32, string name16, string nameLow, string nameHigh) {
        ulong bits = (ulong)(values & mask);
        if (bits == 0) {
            return;
        }
        ulong m = (ulong)mask;
        ulong highHalf = m & (m << 2);
        ulong word = m & ~highHalf;
        ulong lowByte = m & ~(m << 1);
        if ((bits & highHalf) != 0) {
            names.Add(name32);
        } else if (bits == word) {
            names.Add(name16);
        } else {
            names.Add(bits == lowByte ? nameLow : nameHigh);
        }
    }

    private static void WordRegister(List<string> names, CpuValues values, CpuValues mask,
        string name32, string name16) {
        ulong bits = (ulong)(values & mask);
        if (bits == 0) {
            return;
        }
        ulong m = (ulong)mask;
        names.Add((bits & (m & (m << 2))) != 0 ? name32 : name16);
    }

    private static void Flag(List<string> names, CpuValues values, CpuValues flag, string name) {
        if ((values & flag) != CpuValues.None) {
            names.Add(name);
        }
    }

    private sealed class EntryState {
        public CpuValues Inputs;
        public CpuValues Changes;
        public CpuValues Outputs;
        public bool UnknownWay;
        public bool Returns;
    }

    private sealed record EntrySite(CfgCodePartitionEntry Entry, IrBlock Block, int[] EntryVersions);

    private sealed class CallSite {
        public required int[] DefinedVersions { get; init; }
        /// <summary>The version of every value right after the call: the defined version when the
        /// call defines it, else the version that reaches the call, so values restored or
        /// preserved across the call count too.</summary>
        public required int[] VersionsAfter { get; init; }
        /// <summary>The node the call's observed continuation edge targets, or
        /// <see langword="null"/> when no continuation was observed. A callee return that lands
        /// on it reads this site's <see cref="LiveAfterCall"/>.</summary>
        public required ICfgNode? ContinuationNode { get; init; }
        /// <summary>The statement or terminator the call is. The reaching version's use at it -
        /// the callee's own inputs - is not part of <see cref="LiveAfterCall"/>.</summary>
        public required object Node { get; init; }
        /// <summary>The block the call sits in.</summary>
        public required IrBlock Block { get; init; }
        /// <summary>The call's statement index in <see cref="Block"/> - the block's statement
        /// count for a call that is the terminator, so only uses after it count.</summary>
        public required int StatementIndex { get; init; }
        /// <summary>The blocks control can reach after the call's continuation - plus
        /// <see cref="Block"/> itself when a cycle returns to it, since its earlier statements
        /// then run again after the call.</summary>
        public HashSet<IrBlock>? AfterBlocks { get; set; }
        public required List<CfgCodePartitionEntry> Targets { get; init; }
        public required bool Resolved { get; init; }
        /// <summary>Whether a continuation was observed after the call during discovery.</summary>
        public required bool ObservedContinuation { get; init; }
        /// <summary>Whether the call has an observed redirected-return landing.</summary>
        public required bool RedirectedReturn { get; init; }
        /// <summary>Whether the site is an <c>int</c> that pushes the FLAGS image before its
        /// target runs: it needs the six status flags and DF on top of the targets' inputs.</summary>
        public required bool WritesInterruptFrame { get; init; }
        /// <summary>Whether the site's own statement effects carry declared reads the targets'
        /// inputs do not cover - an EXEC child's call, whose contract inputs and frame reads add
        /// to the child entries' inputs (<see cref="ExecChildTargets"/>).</summary>
        public bool IncludeStatementReads { get; init; }
        /// <summary>The bytes still needed right after the call, over the versions
        /// <see cref="VersionsAfter"/> - fixed-point state: starts at <see cref="CpuValues.None"/>
        /// and only grows.</summary>
        public CpuValues LiveAfterCall { get; set; }
        /// <summary>The methods whose return exits land on <see cref="ContinuationNode"/>: their
        /// landing demand reads <see cref="LiveAfterCall"/>.</summary>
        public List<MethodData> LandingMethods { get; } = [];
    }

    private enum ExitKind { Return, Abort, TransferResolved, TransferUnresolved }

    private sealed class ExitSite {
        public required IrExit Exit { get; init; }
        public required IrBlock Block { get; init; }
        public required ExitKind Kind { get; init; }
        public required bool Iret { get; init; }
        public List<CfgCodePartitionEntry> Targets { get; init; } = [];
        /// <summary>For a return exit: the modelled call sites whose continuation node is an
        /// observed landing of the <c>ret</c>.</summary>
        public List<CallSite> LandingCalls { get; init; } = [];
        /// <summary>For a return exit: every observed landing of the <c>ret</c> mapped to a
        /// partition entry or to the continuation node of a modelled call site, so
        /// <see cref="Targets"/> and <see cref="LandingCalls"/> together are the complete landing
        /// demand. False when a landing is neither - an iret into arbitrary code or code that is
        /// not generated.</summary>
        public bool LandingsResolved { get; init; }
    }

    private sealed class MethodData {
        public required PreparedMethod Source { get; init; }
        public required IrSsa Ssa { get; init; }
        public required IrStackSlots Slots { get; set; }
        public required List<EntrySite> Entries { get; init; }
        public required List<CallSite> Calls { get; init; }
        public required List<ExitSite> Exits { get; init; }
        public required Dictionary<IrStatement, CallSite> CallByStatement { get; init; }
        public required Dictionary<IrTerminator, CallSite> CallByTerminator { get; init; }
        public required Dictionary<IrExit, ExitSite> ExitByTerminator { get; init; }
        public required Dictionary<int, CfgCodePartitionEntry> EntryByLoadOffset { get; init; }
        /// <summary>The partition entries the method's terminators (exits and conditional
        /// branches alike) hand control to, resolved or not.</summary>
        public required List<CfgCodePartitionEntry> TransferTargets { get; init; }
        /// <summary>Whether a terminator transfers to a target that maps to no partition
        /// entry - an edge into unmodelled code.</summary>
        public bool HasUnresolvedTransfer { get; set; }
        /// <summary>Whether any statement of the method's instructions can fault into a handler.</summary>
        public bool HasFaultEdge => Source.IrMethod.Blocks
            .SelectMany(block => block.Statements)
            .Any(statement => statement.Effects.MayFault);
        public bool Returns { get; set; }
        /// <summary>The method's block graph, built on first use - only methods that hold a call
        /// site a return lands on need it, to bound after-call liveness by position.</summary>
        public BlockGraph<IrBlock>? BlockGraph { get; set; }
        /// <summary><see langword="true"/> when a state the memos were computed under changed and
        /// they must be cleared before the next <see cref="Needed"/> or <see cref="Preservation"/> call.</summary>
        public bool MemoStale { get; set; }
        public readonly Dictionary<int, CpuValues> NeededMemo = new();
        public required IrPreservation Preservation { get; set; }
    }

    private void Preprocess(IReadOnlyList<PreparedMethod> methods) {
        foreach (PreparedMethod source in methods) {
            IrStackSlots slots = source.StackSlots;
            _methodByName[source.Method.MethodName] = new MethodData {
                Source = source,
                Ssa = source.Ssa,
                Slots = slots,
                Entries = [],
                Calls = [],
                Exits = [],
                CallByStatement = [],
                CallByTerminator = [],
                ExitByTerminator = [],
                EntryByLoadOffset = [],
                TransferTargets = [],
                Preservation = CreatePreservation(source, slots)
            };
        }
        foreach (MethodData method in _methodByName.Values) {
            PreprocessMethod(method);
            _methods.Add(method);
        }
        // Exit classification runs in a second pass: a return landing can be the continuation
        // node of a call site in any method, so every call site must be indexed first, and a
        // transfer target resolves through the target method's EntryByLoadOffset, which exists
        // only once that method has been preprocessed.
        foreach (MethodData method in _methods) {
            PreprocessExits(method);
        }
    }

    private void PreprocessMethod(MethodData method) {
        IrSsa ssa = method.Ssa;
        IrMethod irMethod = method.Source.IrMethod;
        MethodPlan plan = method.Source.Method;
        foreach (CfgCodePartitionEntry entry in plan.Entries) {
            method.EntryByLoadOffset[_context.GetEntryLoadOffset(plan.Partition, entry.Node)] = entry;
        }
        foreach (IrEntry entry in irMethod.Entries
                .Where(entry => _entryByNode.ContainsKey(entry.Node))) {
            CfgCodePartitionEntry mapped = _entryByNode[entry.Node];
            int[] versions = new int[ValueCount];
            for (int v = 0; v < ValueCount; v++) {
                versions[v] = ssa.VersionBefore(entry.Block, 0, (IrSsaValue)v);
            }
            method.Entries.Add(new EntrySite(mapped, entry.Block, versions));
            _methodByEntry[mapped] = method;
        }
        foreach (IrBlock block in irMethod.Blocks) {
            for (int statementIndex = 0; statementIndex < block.Statements.Count; statementIndex++) {
                IrStatement statement = block.Statements[statementIndex];
                IReadOnlyList<SegmentedAddress>? execChildren = ExecChildTargets(statement.Node);
                if (statement.Node is CheckExternalEventsNode
                    || statement.Origin is not CfgInstruction instruction
                    || (execChildren is null
                        && (!instruction.IsCall
                            || ServiceContractEffects.DescribesSite(statement.Node)))) {
                    continue;
                }
                List<CfgCodePartitionEntry> targets = [];
                bool resolved = true;
                if (execChildren is { } childAddresses) {
                    foreach (SegmentedAddress address in childAddresses) {
                        if (_entryByAddress.TryGetValue(address,
                                out CfgCodePartitionEntry? mapped)) {
                            targets.Add(mapped);
                        } else {
                            resolved = false;
                        }
                    }
                } else {
                    foreach (ICfgNode target in HardwareInterruptEffectBuilder.TransferTargets(instruction)) {
                        if (_entryByNode.TryGetValue(target, out CfgCodePartitionEntry? mapped)) {
                            targets.Add(mapped);
                        } else {
                            resolved = false;
                        }
                    }
                    targets = SectionCallTargets(IrAstChildren.Descendants(statement.Node), targets);
                }
                resolved &= targets.Count > 0;
                int[] defined = new int[ValueCount];
                Array.Fill(defined, -1);
                foreach ((IrSsaValue value, int version) in ssa.Definitions(statement)) {
                    if ((int)value < ValueCount) {
                        defined[(int)value] = version;
                    }
                }
                int[] versionsAfter = VersionsAfterOf(ssa, defined, block, statementIndex);
                CallContinuation continuation = _context.ResolveCallContinuation(instruction);
                CallSite site = new() {
                    DefinedVersions = defined, VersionsAfter = versionsAfter,
                    ContinuationNode = continuation.ObservedContinuationEdge?.Target,
                    Node = statement, Block = block, StatementIndex = statementIndex,
                    Targets = targets, Resolved = resolved,
                    ObservedContinuation = continuation.ObservedContinuationEdge is not null,
                    RedirectedReturn = continuation.RedirectedContinuationEdges.Count > 0,
                    WritesInterruptFrame = execChildren is not null
                        || IrAstChildren.Descendants(statement.Node)
                            .Any(IsInterruptCallSite),
                    IncludeStatementReads = execChildren is not null
                };
                method.Calls.Add(site);
                method.CallByStatement[statement] = site;
                IndexCallSite(method, site);
                if (resolved) {
                    foreach (CfgCodePartitionEntry target in targets) {
                        if (!_callersOf.TryGetValue(target, out List<(MethodData, CallSite)>? callers)) {
                            callers = [];
                            _callersOf[target] = callers;
                        }
                        callers.Add((method, site));
                        AddReader(target, method);
                    }
                }
            }
            if (block.HasTerminator
                && block.Terminator is not IrExit
                && block.Terminator.Origin is CfgInstruction { IsCall: true } callInstruction
                && !IrAstChildren.TerminatorNodes(block.Terminator)
                    .Any(ServiceContractEffects.DescribesSite)) {
                List<CfgCodePartitionEntry> targets = [];
                bool resolved = true;
                foreach (ICfgNode target in HardwareInterruptEffectBuilder.TransferTargets(callInstruction)) {
                    if (_entryByNode.TryGetValue(target, out CfgCodePartitionEntry? mapped)) {
                        targets.Add(mapped);
                    } else {
                        resolved = false;
                    }
                }
                targets = SectionCallTargets(IrAstChildren.TerminatorNodes(block.Terminator), targets);
                resolved &= targets.Count > 0;
                int[] defined = new int[ValueCount];
                Array.Fill(defined, -1);
                foreach ((IrSsaValue value, int version) in ssa.Definitions(block.Terminator)) {
                    if ((int)value < ValueCount) {
                        defined[(int)value] = version;
                    }
                }
                int[] versionsAfter = VersionsAfterOf(ssa, defined, block, block.Statements.Count);
                CallContinuation continuation = _context.ResolveCallContinuation(callInstruction);
                CallSite site = new() {
                    DefinedVersions = defined, VersionsAfter = versionsAfter,
                    ContinuationNode = continuation.ObservedContinuationEdge?.Target,
                    Node = block.Terminator, Block = block,
                    StatementIndex = block.Statements.Count,
                    Targets = targets, Resolved = resolved,
                    ObservedContinuation = continuation.ObservedContinuationEdge is not null,
                    RedirectedReturn = continuation.RedirectedContinuationEdges.Count > 0,
                    WritesInterruptFrame = IrAstChildren.TerminatorNodes(block.Terminator)
                        .Any(IsInterruptCallSite)
                };
                method.Calls.Add(site);
                method.CallByTerminator[block.Terminator] = site;
                IndexCallSite(method, site);
                if (resolved) {
                    foreach (CfgCodePartitionEntry target in targets) {
                        if (!_callersOf.TryGetValue(target, out List<(MethodData, CallSite)>? callers)) {
                            callers = [];
                            _callersOf[target] = callers;
                        }
                        callers.Add((method, site));
                        AddReader(target, method);
                    }
                }
            }
        }
    }

    /// <summary>The call targets a dispatch section's site really has: when <paramref name="nodes"/>
    /// holds one <see cref="ProcedureCallHelperNode"/> ending in a <see cref="MethodGroupNode"/>, the
    /// section runs only after its runtime target check singled that entry out, so the call is
    /// charged by that entry alone instead of every target the instruction observed. The group maps
    /// to an entry exactly the way <see cref="TransferEmitter.FunctionReference"/> builds it:
    /// method name plus load offset. Any other shape - or a group naming none of the resolved
    /// targets - keeps <paramref name="targets"/>.</summary>
    private List<CfgCodePartitionEntry> SectionCallTargets(IEnumerable<IVisitableAstNode> nodes,
        List<CfgCodePartitionEntry> targets) {
        if (targets.Count <= 1) {
            return targets;
        }
        if (nodes.OfType<ProcedureCallHelperNode>().ToList() is not [ProcedureCallHelperNode call]
            || call.Arguments is not [.., MethodGroupNode group]) {
            return targets;
        }
        CfgCodePartitionEntry? own = targets.FirstOrDefault(target => {
            CfgCodePartition partition = _context.GetPartition(target.Node);
            return _context.GetMethodName(partition) == group.MethodName
                && _context.GetEntryLoadOffset(partition, target.Node) == group.LoadOffset;
        });
        return own is null ? targets : [own];
    }

    /// <summary>The version of every value right after a call at
    /// <paramref name="statementIndex"/> in <paramref name="block"/>: the version the call
    /// defines when it has one, else the version that reaches it.</summary>
    private static int[] VersionsAfterOf(IrSsa ssa, int[] defined, IrBlock block, int statementIndex) {
        int[] versionsAfter = new int[ValueCount];
        for (int v = 0; v < ValueCount; v++) {
            versionsAfter[v] = defined[v] >= 0
                ? defined[v]
                : ssa.VersionBefore(block, statementIndex, (IrSsaValue)v);
        }
        return versionsAfter;
    }

    /// <summary>Indexes <paramref name="site"/> by its observed continuation node so a return
    /// landing on it can find the site's <see cref="CallSite.LiveAfterCall"/>.</summary>
    private void IndexCallSite(MethodData method, CallSite site) {
        if (site.ContinuationNode is not { } continuationNode) {
            return;
        }
        if (!_callSitesByContinuation.TryGetValue(continuationNode,
                out List<(MethodData, CallSite)>? landing)) {
            landing = [];
            _callSitesByContinuation[continuationNode] = landing;
        }
        landing.Add((method, site));
    }

    private void PreprocessExits(MethodData method) {
        foreach (IrBlock block in method.Source.IrMethod.Blocks) {
            if (!block.HasTerminator) {
                continue;
            }
            foreach (IVisitableAstNode root in IrAstChildren.TerminatorNodes(block.Terminator)) {
                (bool found, bool resolved) = CollectTransferNodes(root, method.TransferTargets,
                    TransferTargetOf);
                method.HasUnresolvedTransfer |= found && !resolved;
            }
            if (block.Terminator is IrExit exit) {
                ExitSite site = ClassifyExit(exit, block, method);
                method.Exits.Add(site);
                method.ExitByTerminator[exit] = site;
                method.Returns |= site.Kind is ExitKind.Return or ExitKind.TransferResolved;
                if (site.Kind is ExitKind.Return or ExitKind.TransferResolved) {
                    foreach (CfgCodePartitionEntry target in site.Targets) {
                        AddReader(target, method);
                    }
                }
                if (site.Kind is ExitKind.TransferResolved) {
                    foreach (CfgCodePartitionEntry target in site.Targets) {
                        if (!_transfersTo.TryGetValue(target,
                                out List<(MethodData, ExitSite)>? transfers)) {
                            transfers = [];
                            _transfersTo[target] = transfers;
                        }
                        transfers.Add((method, site));
                    }
                }
            }
        }
        foreach (EntrySite site in method.Entries) {
            _states[site.Entry].Returns = method.Returns;
        }
    }

    private ExitSite ClassifyExit(IrExit exit, IrBlock block, MethodData method) {
        bool iret = Contains<InterruptReturnNode>(exit.Transfer);
        if (iret || exit.Origin is CfgInstruction { IsReturn: true }) {
            List<CfgCodePartitionEntry> landings = [];
            List<CallSite> landingCalls = [];
            bool landingsResolved = exit.Origin is CfgInstruction instruction
                && ReturnLandings(instruction, landings, landingCalls, method);
            return new ExitSite {
                Exit = exit, Block = block, Kind = ExitKind.Return, Iret = iret,
                Targets = landings, LandingCalls = landingCalls,
                LandingsResolved = landingsResolved
            };
        }
        List<CfgCodePartitionEntry> targets = [];
        (bool foundTransfer, bool resolved) = CollectTransferNodes(exit.Transfer, targets, TransferTargetOf);
        if (foundTransfer) {
            return new ExitSite {
                Exit = exit, Block = block, Iret = false,
                Kind = resolved ? ExitKind.TransferResolved : ExitKind.TransferUnresolved,
                Targets = targets
            };
        }
        ExitKind kind = ExitKind.TransferUnresolved;
        if (Contains<FailAsUntestedNode>(exit.Transfer) || Contains<ThrowNode>(exit.Transfer)
                || Contains<ReturnNode>(exit.Transfer)) {
            kind = ExitKind.Abort;
        }
        return new ExitSite {
            Exit = exit, Block = block, Iret = false,
            Kind = kind
        };
    }

    /// <summary>The partition entry an exit's method call or cyclic jump reaches: a name in the
    /// method table whose <paramref name="loadOffset"/> (<see langword="null"/> for no arguments)
    /// maps to an entry.</summary>
    private bool ExitTargetOf(string methodName, int? loadOffset,
        [NotNullWhen(true)] out CfgCodePartitionEntry? entry) {
        if (!_methodByName.TryGetValue(methodName, out MethodData? target)) {
            entry = null;
            return false;
        }
        return target.EntryByLoadOffset.TryGetValue(loadOffset ?? 0, out entry);
    }

    private delegate bool TransferResolver(string methodName, int? loadOffset,
        [NotNullWhen(true)] out CfgCodePartitionEntry? entry);

    /// <summary>The partition entries <paramref name="transfer"/> hands control to: the resolved
    /// targets of its method calls and cyclic jumps. <c>Found</c> is false when the node transfers
    /// nowhere, <c>Resolved</c> false when a transfer's target cannot be mapped to an entry - the
    /// hand-off then reaches unmodelled code.</summary>
    private (bool Found, bool Resolved) CollectTransferNodes(IVisitableAstNode transfer,
        List<CfgCodePartitionEntry> targets, TransferResolver resolver) {
        bool foundTransfer = false;
        bool resolved = true;
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(transfer)) {
            switch (node) {
                case MethodCallNode { PropertyPath: null } call
                    when IrAstChildren.IsGeneratedMethodTransfer(call): {
                    foundTransfer = true;
                    bool offsetKnown = call.Arguments is [] or [OffsetLiteralNode];
                    int? loadOffset = call.Arguments is [OffsetLiteralNode literal]
                        ? literal.Value
                        : null;
                    if (offsetKnown
                        && resolver(call.MethodName, loadOffset,
                            out CfgCodePartitionEntry? entry)) {
                        targets.Add(entry);
                    } else {
                        resolved = false;
                    }
                    break;
                }
                case CyclicJumpNode cyclic: {
                    foundTransfer = true;
                    if (resolver(cyclic.MethodName, cyclic.LoadOffset,
                            out CfgCodePartitionEntry? entry)) {
                        targets.Add(entry);
                    } else {
                        resolved = false;
                    }
                    break;
                }
                case MethodGroupNode group: {
                    foundTransfer = true;
                    if (resolver(group.MethodName, group.LoadOffset,
                            out CfgCodePartitionEntry? entry)) {
                        targets.Add(entry);
                    } else {
                        resolved = false;
                    }
                    break;
                }
            }
        }
        return (foundTransfer, resolved);
    }

    /// <summary>The partition entry <paramref name="methodName"/>'s transfer at
    /// <paramref name="loadOffset"/> reaches; a <see langword="null"/> offset is the method's
    /// primary entry.</summary>
    private bool TransferTargetOf(string methodName, int? loadOffset,
        [NotNullWhen(true)] out CfgCodePartitionEntry? entry) {
        if (!_methodByName.TryGetValue(methodName, out MethodData? target)) {
            entry = null;
            return false;
        }
        if (loadOffset is null) {
            entry = target.Source.Method.PrimaryEntry;
            return true;
        }
        return target.EntryByLoadOffset.TryGetValue(loadOffset.Value, out entry);
    }

    /// <summary>Fills <paramref name="targets"/> with the landings of the return
    /// <paramref name="instruction"/> that are partition entries, and <paramref name="landingCalls"/>
    /// with the modelled call sites whose continuation node it lands on. False when a landing is
    /// neither - an iret into arbitrary code or code that is not generated - or when no landing
    /// was recorded.</summary>
    private bool ReturnLandings(CfgInstruction instruction, List<CfgCodePartitionEntry> targets,
        List<CallSite> landingCalls, MethodData method) {
        bool resolved = false;
        foreach (ICfgNode target in HardwareInterruptEffectBuilder.TransferTargets(instruction)) {
            if (_entryByNode.TryGetValue(target, out CfgCodePartitionEntry? mapped)) {
                targets.Add(mapped);
            } else if (_callSitesByContinuation.TryGetValue(target,
                    out List<(MethodData Caller, CallSite Site)>? sites)) {
                foreach ((_, CallSite site) in sites) {
                    landingCalls.Add(site);
                    site.LandingMethods.Add(method);
                }
            } else {
                return false;
            }
            resolved = true;
        }
        return resolved;
    }

    private static bool IsReturnExit(IrExit exit) =>
        Contains<InterruptReturnNode>(exit.Transfer)
        || exit.Origin is CfgInstruction { IsReturn: true };

    private static bool Contains<T>(IVisitableAstNode node) where T : IVisitableAstNode =>
        node is T || IrAstChildren.Descendants(node).OfType<T>().Any();

    /// <summary>The entries with a way in that no call site explains - the seeded unknown ways:
    /// the program entry, the execution context entries, and the target of every
    /// transfer other than <see cref="CfgCodePartitionTransferKind.CallOut"/>.</summary>
    private void SeedUnknownWays() {
        foreach ((_, CfgCodePartitionEntry entry) in _entryByNode) {
            if (entry.Address.Equals(_context.Program.Inputs.ProgramEntryPoint)) {
                _states[entry].UnknownWay = true;
            } else if (entry.Kind == CfgCodePartitionEntryKind.ExecutionContextEntry) {
                _states[entry].UnknownWay = true;
            }
        }
        foreach (CfgCodePartitionTransfer transfer in _context.Program.Transfers
                .Where(transfer => transfer.Kind != CfgCodePartitionTransferKind.CallOut
                    && _entryByNode.ContainsKey(transfer.TargetNode))) {
            _states[_entryByNode[transfer.TargetNode]].UnknownWay = true;
        }
    }

    private void AddReader(CfgCodePartitionEntry entry, MethodData method) {
        if (!_readersOf.TryGetValue(entry, out HashSet<MethodData>? readers)) {
            readers = [];
            _readersOf[entry] = readers;
        }
        readers.Add(method);
    }

    /// <summary>Schedules every entry computation of <paramref name="method"/> and the live-after
    /// recompute of every call target its <see cref="Needed"/> may feed; its memos are dropped at
    /// the next computation they feed.</summary>
    private void MarkDirtyMethod(MethodData method, HashSet<CfgCodePartitionEntry> sitesDirty,
        HashSet<CfgCodePartitionEntry> outputsDirty) {
        method.MemoStale = true;
        foreach (EntrySite site in method.Entries) {
            sitesDirty.Add(site.Entry);
        }
        foreach (CallSite call in method.Calls) {
            foreach (CfgCodePartitionEntry target in call.Targets) {
                outputsDirty.Add(target);
            }
        }
    }

    /// <summary>Drops <paramref name="method"/>'s memos when a state they were computed under
    /// changed; called lazily at the point a computation is about to consult them, so a method
    /// marked after its slot in this round's order still serves the values it computed there.</summary>
    private void ClearMemosIfStale(MethodData method) {
        if (!method.MemoStale) {
            return;
        }
        method.NeededMemo.Clear();
        method.Preservation = CreatePreservation(method.Source, method.Slots);
        method.MemoStale = false;
    }

    /// <summary>One <see cref="IrPreservation"/> per method, recreated when the memos go stale:
    /// the transfer changes it consults move between iterations.</summary>
    private IrPreservation CreatePreservation(PreparedMethod source, IrStackSlots slots) =>
        new(source.IrMethod, source.Ssa, slots,
            statement => TransferChangesOf(source.Method.MethodName, statement));

    /// <summary>The bytes a call site of <paramref name="methodName"/> may change for
    /// <see cref="IrPreservation"/>: the resolved targets' changes, every register for an
    /// unresolved call, null for any other statement (an event check falls back to its node's
    /// effect).</summary>
    private CpuValues? TransferChangesOf(string methodName, IrStatement statement) {
        if (!_methodByName.TryGetValue(methodName, out MethodData? method)
            || !method.CallByStatement.TryGetValue(statement, out CallSite? call)) {
            return null;
        }
        return call.Resolved ? TargetsChanges(call) : CpuValues.All;
    }

    private void Iterate(Action<int> roundStarted) {
        // Each round recomputes only the entry sites and outputs whose inputs changed: a site when
        // an input, a change or an output it reads moved; an outputs when its entry's changes or a
        // caller's liveness inputs moved. Writes happen in the same positions as a full recompute,
        // and a skipped site reads unchanged inputs and would rewrite its stored value, so the
        // result is identical.
        HashSet<CfgCodePartitionEntry> sitesDirty = new(_methodByEntry.Keys);
        HashSet<CfgCodePartitionEntry> outputsDirty = new(_states.Keys);
        for (int iteration = 0; ; iteration++) {
            roundStarted(iteration + 1);
            bool changed = false;
            foreach (MethodData method in _methods) {
                foreach (EntrySite site in method.Entries) {
                    if (!sitesDirty.Remove(site.Entry)) {
                        continue;
                    }
                    ClearMemosIfStale(method);
                    EntryState state = _states[site.Entry];
                    CpuValues inputs = ComputeInputs(method, site);
                    if (inputs != state.Inputs) {
                        state.Inputs = inputs;
                        changed = true;
                        if (_readersOf.TryGetValue(site.Entry, out HashSet<MethodData>? readers)) {
                            foreach (MethodData reader in readers) {
                                MarkDirtyMethod(reader, sitesDirty, outputsDirty);
                            }
                        }
                    }
                    CpuValues changes = ComputeChanges(method);
                    if (changes != state.Changes) {
                        state.Changes = changes;
                        changed = true;
                        outputsDirty.Add(site.Entry);
                        if (_readersOf.TryGetValue(site.Entry, out HashSet<MethodData>? readers)) {
                            foreach (MethodData reader in readers) {
                                MarkDirtyMethod(reader, sitesDirty, outputsDirty);
                            }
                        }
                    }
                }
            }
            foreach ((CfgCodePartitionEntry entry, EntryState state) in _states) {
                if (!outputsDirty.Remove(entry)) {
                    continue;
                }
                CpuValues outputs = state.Changes & LiveAfter(entry, state);
                if (outputs != state.Outputs) {
                    state.Outputs = outputs;
                    changed = true;
                    if (_methodByEntry.TryGetValue(entry, out MethodData? owner)) {
                        MarkDirtyMethod(owner, sitesDirty, outputsDirty);
                    }
                }
            }
            // The live-after-call of every site a return can land on is fixed-point state too:
            // it only grows. A grown value widens the landing demand of the exits that land on
            // it, so the methods that own those exits - and their unknown-way entries' outputs -
            // recompute.
            foreach (List<(MethodData Caller, CallSite Site)> sites
                     in _callSitesByContinuation.Values) {
                foreach ((MethodData caller, CallSite call) in sites) {
                    ClearMemosIfStale(caller);
                    CpuValues live = call.LiveAfterCall;
                    for (int v = 0; v < ValueCount; v++) {
                        live |= NeededAfterCall(caller, (IrSsaValue)v, call, []);
                    }
                    if (live != call.LiveAfterCall) {
                        call.LiveAfterCall = live;
                        changed = true;
                        foreach (MethodData landing in call.LandingMethods) {
                            MarkDirtyMethod(landing, sitesDirty, outputsDirty);
                            foreach (EntrySite entrySite in landing.Entries
                                    .Where(entrySite => _states[entrySite.Entry].UnknownWay)) {
                                outputsDirty.Add(entrySite.Entry);
                            }
                        }
                    }
                }
            }
            if (!changed) {
                Iterations = iteration + 1;
                return;
            }
            if (iteration >= MaxIterations) {
                // The sets are not monotone (a use can shrink under larger target changes), so
                // convergence is not guaranteed: fall back to the conservative answer.
                foreach (EntryState state in _states.Values) {
                    state.Inputs = Reportable;
                    state.Changes = Reportable;
                    state.Outputs = Reportable;
                }
                Iterations = iteration + 1;
                return;
            }
        }
    }

    private CpuValues ComputeInputs(MethodData method, EntrySite site) {
        CpuValues inputs = CpuValues.None;
        for (int v = 0; v < ValueCount; v++) {
            inputs |= Needed(method, (IrSsaValue)v, site.EntryVersions[v], []);
        }
        return inputs & Reportable;
    }

    private CpuValues ComputeChanges(MethodData method) {
        CpuValues changes = CpuValues.None;
        foreach (ExitSite exit in method.Exits) {
            switch (exit.Kind) {
                case ExitKind.Return:
                case ExitKind.TransferResolved:
                    for (int v = 0; v < ValueCount; v++) {
                        IrSsaValue value = (IrSsaValue)v;
                        int version = method.Ssa.VersionBefore(exit.Block,
                            exit.Block.Statements.Count, value);
                        changes |= IrSsaValues.MaskOf(value)
                            & ~method.Preservation.Preserved(value, version);
                    }
                    if (exit.Iret) {
                        changes |= CpuValues.StatusFlags | CpuValues.Direction;
                    }
                    // A resolved transfer continues in its target, so the target's changes are
                    // this method's. A return's landing is the caller's continuation: its changes
                    // are the caller's own, and adding them here would mask the bytes the caller
                    // still demands back through the call.
                    if (exit.Kind == ExitKind.TransferResolved) {
                        foreach (CfgCodePartitionEntry target in exit.Targets) {
                            changes |= _states[target].Changes;
                        }
                    }
                    break;
                case ExitKind.TransferUnresolved:
                    changes |= Reportable;
                    break;
            }
        }
        return changes & Reportable;
    }

    /// <summary>What callers read after <paramref name="entry"/>: the union over its call sites of
    /// the bytes still needed after the call. The program entry's non-call way is the terminal
    /// observation: a program-ending hlt reads no register, so it falls through to the callers'
    /// union. An execution context entry returns into code that still observes the machine, so it
    /// keeps the whole reportable demand. Any other non-call way in - a transfer target - still
    /// returns only through the method's return exits, so what is read after it is the union of
    /// their landing demands (<see cref="LandingInputs"/>); it widens back to every reportable
    /// value when a return's landing is unresolved - a machine-leaving way out the model does not
    /// describe - or when the method has a transfer exit, which hands control elsewhere before any
    /// return runs.</summary>
    private CpuValues LiveAfter(CfgCodePartitionEntry entry, EntryState state) {
        if (state.UnknownWay
            && !entry.Address.Equals(_context.Program.Inputs.ProgramEntryPoint)) {
            if (entry.Kind == CfgCodePartitionEntryKind.ExecutionContextEntry
                || !_methodByEntry.TryGetValue(entry, out MethodData? method)) {
                return Reportable;
            }
            if (method.Exits.Any(exit => exit.Kind is ExitKind.TransferResolved
                    or ExitKind.TransferUnresolved
                    || (exit.Kind == ExitKind.Return && !exit.LandingsResolved))) {
                return Reportable;
            }
            CpuValues landings = CpuValues.None;
            foreach (ExitSite exit in method.Exits.Where(exit => exit.Kind == ExitKind.Return)) {
                landings |= LandingInputs(exit);
            }
            return landings;
        }
        if (!_callersOf.TryGetValue(entry, out List<(MethodData, CallSite)>? callers)) {
            return CpuValues.None;
        }
        CpuValues live = CpuValues.None;
        foreach ((MethodData caller, CallSite site) in callers) {
            ClearMemosIfStale(caller);
            for (int v = 0; v < ValueCount; v++) {
                if (site.DefinedVersions[v] >= 0) {
                    live |= Needed(caller, (IrSsaValue)v, site.DefinedVersions[v], []);
                }
            }
        }
        return live;
    }

    /// <summary>The bytes the resolved targets of <paramref name="call"/> input.</summary>

    /// <summary>The bytes of a value still needed right after <paramref name="call"/> runs: the
    /// <see cref="Needed"/> of the version after the call, except that a value the call does not
    /// define reaches the continuation under the version that reaches the call - and that
    /// version's use at the call itself (the callee's inputs) is not part of the after-call
    /// demand. Not memoized: it differs from <see cref="Needed"/> on exactly that one use.</summary>
    private CpuValues NeededAfterCall(MethodData method, IrSsaValue value, CallSite call,
        HashSet<int> visiting) {
        int version = call.VersionsAfter[(int)value];
        if (version < 0) {
            return CpuValues.None;
        }
        if (call.DefinedVersions[(int)value] >= 0) {
            return Needed(method, value, version, visiting);
        }
        visiting.Add(version);
        bool cyclic = AfterBlocks(method, call).Contains(call.Block);
        SegmentedAddress callAddress = call.Node is IrStatement callStatement
            ? callStatement.Origin.Address
            : ((IrTerminator)call.Node).Origin.Address;
        CpuValues result = CpuValues.None;
        foreach (IrSsaUse use in method.Ssa.Uses(version)) {
            if (!cyclic
                && (ReferenceEquals(use.Statement, call.Node)
                    || ReferenceEquals(use.Terminator, call.Node)
                    || use.Statement?.Origin.Address.Equals(callAddress) == true
                    || use.Terminator?.Origin.Address.Equals(callAddress) == true)) {
                continue;
            }
            if (!MayFollowCall(method, call, use)) {
                continue;
            }
            result |= NeededUse(method, value, use, visiting);
        }
        visiting.Remove(version);
        return result;
    }

    /// <summary>Whether <paramref name="use"/> can run after <paramref name="call"/>: a use in
    /// another block counts when the block is reachable from the call's continuation; a use in the
    /// call's own block counts when it sits after the call - or anywhere when a cycle returns to
    /// the block, since it then runs again after the call. A phi operand's use sits on the edge
    /// from its predecessor.</summary>
    private static bool MayFollowCall(MethodData method, CallSite call, IrSsaUse use) {
        if (use.Phi is not null) {
            return use.OperandPredecessor is { } predecessor
                && (ReferenceEquals(predecessor, call.Block)
                    || AfterBlocks(method, call).Contains(predecessor));
        }
        if (!ReferenceEquals(use.Block, call.Block)) {
            return AfterBlocks(method, call).Contains(use.Block);
        }
        if (AfterBlocks(method, call).Contains(call.Block)) {
            return true;
        }
        return use.Terminator is not null
            || use.Statement is { } statement
                && call.Block.IndexOfStatement(statement) > call.StatementIndex;
    }

    /// <summary>The blocks reachable from <paramref name="call"/>'s continuation, computed once
    /// per call site.</summary>
    private static HashSet<IrBlock> AfterBlocks(MethodData method, CallSite call) {
        if (call.AfterBlocks is { } cached) {
            return cached;
        }
        method.BlockGraph ??= method.Source.IrMethod.ToBlockGraph();
        HashSet<IrBlock> reachable = [];
        Queue<IrBlock> work = new(method.BlockGraph.GetSuccessors(call.Block));
        while (work.Count > 0) {
            IrBlock next = work.Dequeue();
            if (reachable.Add(next)) {
                foreach (IrBlock successor in method.BlockGraph.GetSuccessors(next)) {
                    work.Enqueue(successor);
                }
            }
        }
        call.AfterBlocks = reachable;
        return reachable;
    }

    /// <summary>The bytes of <paramref name="version"/> that later code may still observe: the
    /// union over its uses, memoized for the current iteration.</summary>
    private CpuValues Needed(MethodData method, IrSsaValue value, int version,
        HashSet<int> visiting) {
        if (version < 0) {
            return CpuValues.None;
        }
        if (method.NeededMemo.TryGetValue(version, out CpuValues memoized)) {
            return memoized;
        }
        if (!visiting.Add(version)) {
            return CpuValues.None;
        }
        CpuValues result = CpuValues.None;
        foreach (IrSsaUse use in method.Ssa.Uses(version)) {
            result |= NeededUse(method, value, use, visiting);
        }
        visiting.Remove(version);
        method.NeededMemo[version] = result;
        return result;
    }

    private CpuValues NeededUse(MethodData method, IrSsaValue value, IrSsaUse use,
        HashSet<int> visiting) {
        CpuValues mask = IrSsaValues.MaskOf(value);
        if (use.Phi is { } phi) {
            return Needed(method, value, phi.Result, visiting);
        }
        if (use.Terminator is { } terminator) {
            return NeededTerminatorUse(method, value, mask, use, terminator, visiting);
        }
        if (use.Statement is not { } statement) {
            return mask;
        }
        if (method.CallByStatement.TryGetValue(statement, out CallSite? call)) {
            if (!call.Resolved) {
                return mask;
            }
            CpuValues result = TargetsInputs(call) & mask;
            if (call.IncludeStatementReads) {
                result |= statement.Effects.Reads & mask;
            }
            int defined = call.DefinedVersions[(int)value];
            if (defined >= 0) {
                result |= Needed(method, value, defined, visiting) & ~TargetsChanges(call);
            }
            return result;
        }
        if (statement.Node is CheckExternalEventsNode) {
            foreach ((IrSsaValue defined, int newVersion) in method.Ssa.Definitions(statement)) {
                if (defined == value) {
                    return Needed(method, value, newVersion, visiting);
                }
            }
            return CpuValues.None;
        }
        if (IrPreservation.IsPushOf(statement, value, out CpuValues pushed)) {
            return NeededPush(method, value, statement, pushed, visiting);
        }
        if (use.ConditionalNewVersion >= 0) {
            CpuValues written = statement.Effects.MustWrite & mask;
            return Needed(method, value, use.ConditionalNewVersion, visiting) & ~written;
        }
        return statement.Effects.Reads & mask;
    }

    private CpuValues NeededTerminatorUse(MethodData method, IrSsaValue value, CpuValues mask,
        IrSsaUse use, IrTerminator terminator, HashSet<int> visiting) {
        if (method.CallByTerminator.TryGetValue(terminator, out CallSite? call)) {
            if (!call.Resolved) {
                return mask;
            }
            CpuValues result = TargetsInputs(call) & mask;
            int defined = call.DefinedVersions[(int)value];
            if (defined >= 0) {
                result |= Needed(method, value, defined, visiting) & ~TargetsChanges(call);
            }
            return result;
        }
        if (terminator is not IrExit exit) {
            CpuValues result = terminator.Effects.Reads & mask;
            if (use.ConditionalNewVersion >= 0) {
                result |= Needed(method, value, use.ConditionalNewVersion, visiting)
                    & ~terminator.Effects.MustWrite & mask;
            }
            return result;
        }
        switch (method.ExitByTerminator[exit].Kind) {
            case ExitKind.Return: {
                CpuValues outputs = CpuValues.None;
                foreach (EntrySite site in method.Entries) {
                    outputs |= _states[site.Entry].Outputs;
                }
                CpuValues changes = CpuValues.None;
                foreach (EntrySite site in method.Entries) {
                    changes |= _states[site.Entry].Changes;
                }
                outputs |= LandingInputs(method.ExitByTerminator[exit]) & changes;
                return outputs & mask;
            }
            case ExitKind.Abort:
                // An abort reads only the parts its transfer references: a terminal hlt observes
                // memory only - the differential gate compares no register - and generated code
                // after a fail-as-untested or a thrown abort reads nothing either, since the run
                // ends failing.
                return AbortReads(exit.Transfer) & mask;
            case ExitKind.TransferResolved:
                return TransferExitReads(method.ExitByTerminator[exit]) & mask;
            default:
                return mask;
        }
    }

    /// <summary>The bytes a return exit must still deliver to what it lands on: the union of the
    /// inputs of every partition entry its <c>ret</c> was observed to reach, plus the
    /// <see cref="CallSite.LiveAfterCall"/> of every modelled call site whose continuation node is
    /// a landing - the bytes still needed after that call, over the versions that reach its
    /// continuation. An unmodelled landing - an iret into arbitrary code or code that is not
    /// generated - adds every reportable value under the observed demand only, since machine
    /// state leaving generated code is observed, not a read generated code does. A byte the
    /// callee did not change keeps its entry value in the machine state either way, so the caller
    /// masks this set by its changes.</summary>
    private CpuValues LandingInputs(ExitSite site) {
        CpuValues inputs = CpuValues.None;
        foreach (CfgCodePartitionEntry target in site.Targets) {
            inputs |= _states[target].Inputs | ObservedEntryReads(target);
        }
        foreach (CallSite call in site.LandingCalls) {
            inputs |= call.LiveAfterCall;
        }
        if (!site.LandingsResolved && _observed) {
            inputs |= Reportable;
        }
        return inputs;
    }

    /// <summary>Whether <paramref name="transfer"/> ends generated code for good - it returns to
    /// the machine and does not fail or throw.</summary>
    private static bool IsMachineLeavingReturn(IVisitableAstNode transfer) =>
        Contains<ReturnNode>(transfer)
        && !Contains<FailAsUntestedNode>(transfer)
        && !Contains<ThrowNode>(transfer);

    /// <summary>The register bytes a machine-leaving return exit reads, or <see langword="null"/>
    /// when <paramref name="exit"/> is not such an exit: the parts its transfer literally
    /// references. A terminal hlt observes memory only, so this is the whole demand.</summary>
    internal CpuValues? TerminalExitReadsOf(string methodName, IrExit exit) =>
        _methodByName.TryGetValue(methodName, out MethodData? data)
        && data.ExitByTerminator.TryGetValue(exit, out ExitSite? site)
        && site.Kind == ExitKind.Abort
        && IsMachineLeavingReturn(exit.Transfer)
            ? AbortReads(exit.Transfer)
            : null;

    /// <summary>The machine bytes an abort exit's message reads: only the register / flag /
    /// segment parts the message literally references. Exit-use and unknown-call effects do not
    /// apply: once the machine leaves generated code nothing in it observes machine state.</summary>
    private static CpuValues AbortReads(IVisitableAstNode transfer) {
        CpuValues reads = CpuValues.None;
        foreach (ValueNode node in IrAstChildren.Descendants(transfer)
                .OfType<ValueNode>().Where(node => node is not MethodCallValueNode)) {
            reads |= IrEffectsVisitor.Of(node, false).Reads;
        }
        return reads;
    }

    /// <summary>A use by a push of <paramref name="value"/>'s bytes: when every read of the push's
    /// slot is a complete pop-assign writing the same bytes back, only what the popped versions
    /// need; otherwise the push spilled the value to memory, so its pushed bytes are needed.</summary>
    private CpuValues NeededPush(MethodData method, IrSsaValue value, IrStatement push,
        CpuValues pushed, HashSet<int> visiting) {
        if (method.Slots.OffsetAfter(push) is not { Kind: IrStackOffsetKind.Known } offset) {
            return pushed;
        }
        IReadOnlyList<IrStackSlotRead> readers =
            method.Slots.ReadsOf(new IrStackSlotDefinition(push, offset.Delta));
        if (readers.Count == 0) {
            return pushed;
        }
        CpuValues result = CpuValues.None;
        foreach (IrStackSlotRead read in readers) {
            IrStatement pop = read.Statement;
            if (!read.Complete || !read.ProvedRam || !IrPreservation.IsPopRestore(pop) || !Defines(method.Ssa, pop, value, out int popVersion)) {
                return pushed;
            }
            foreach (IrStackSlotDefinition definition in read.Definitions) {
                if (!IrPreservation.IsPushOf(definition.Statement, value, out _)) {
                    return pushed;
                }
            }
            result |= Needed(method, value, popVersion, visiting);
        }
        return result & pushed;
    }

    private static bool Defines(IrSsa ssa, IrStatement statement, IrSsaValue value, out int version) {
        foreach ((IrSsaValue defined, int definedVersion) in ssa.Definitions(statement)) {
            if (defined == value) {
                version = definedVersion;
                return true;
            }
        }
        version = -1;
        return false;
    }

    /// <summary>Whether <paramref name="node"/> itself is a frame-writing <c>int</c> site: a
    /// generic <see cref="InterruptCallHelperNode"/> or a <see cref="ProgramInterruptCallHelperNode"/>.</summary>
    private static bool IsInterruptCallSite(IVisitableAstNode node) =>
        node is InterruptCallHelperNode or ProgramInterruptCallHelperNode;

    /// <summary>The observed child entries of a <c>RunsChild</c> service call in
    /// <paramref name="node"/>, or <see langword="null"/> when it holds none: such a site is a
    /// call site whose targets are the child program entries the DOS dispatch callback moved
    /// CS:IP to.</summary>
    private static IReadOnlyList<SegmentedAddress>? ExecChildTargets(IVisitableAstNode node) =>
        IrAstChildren.Descendants(node).OfType<InterruptServiceCallNode>()
            .FirstOrDefault(call => call.Contract.Transfer is ServiceTransferKind.RunsChild)
            ?.ChildEntries;

    /// <summary>The machine values the site reads at call time: its resolved targets' inputs,
    /// plus the six status flags and DF when it is an <c>int</c> pushing the FLAGS image.</summary>
    private CpuValues TargetsInputs(CallSite call) {
        CpuValues inputs = CpuValues.None;
        foreach (CfgCodePartitionEntry target in call.Targets) {
            inputs |= _states[target].Inputs | ObservedEntryReads(target);
        }
        if (call.WritesInterruptFrame) {
            inputs |= CpuValues.StatusFlags | CpuValues.Direction;
        }
        return inputs;
    }

    private CpuValues TargetsChanges(CallSite call) {
        CpuValues changes = CpuValues.None;
        foreach (CfgCodePartitionEntry target in call.Targets) {
            changes |= _states[target].Changes;
        }
        return changes;
    }

}
