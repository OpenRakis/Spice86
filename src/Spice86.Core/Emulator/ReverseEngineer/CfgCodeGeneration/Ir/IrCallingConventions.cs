namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

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
/// unresolved call needs everything); a push feeding only complete pop-assigns of the same register
/// needs what each popped version needs (other pushes make the value escape into memory); an event
/// check only forwards through the version it defines (the hardware handler's own reads are not the
/// method's inputs); a partial definition forwards the bytes it does not write; a return exit needs
/// the bytes the method's entries output; an abort exit (a <c>FailAsUntested</c>, a
/// <see cref="ThrowNode"/>, or a return that leaves generated code like <c>Hlt</c> - nothing in
/// generated code observes machine state after it) needs only the bytes its message parts read; a
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
/// <para>- <c>OutputsOf(E)</c> is <c>EffectOf(E).Writes</c> intersected with what is live after E: every
/// reported value when E has a non-call way in (program entry, execution context entry, a transfer
/// other than <see cref="CfgCodePartitionTransferKind.CallOut"/>), else the union over the call
/// sites targeting E of the bytes needed after each call.</para>
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
    private readonly Dictionary<CfgCodePartitionEntry, EntryState> _states = new();
    private readonly Dictionary<string, MethodData> _methodByName = new();
    private readonly Dictionary<CfgCodePartitionEntry, List<(MethodData Caller, CallSite Site)>> _callersOf = new();
    private readonly Dictionary<CfgCodePartitionEntry, MethodData> _methodByEntry = new();
    private readonly Dictionary<CfgCodePartitionEntry, HashSet<MethodData>> _readersOf = new();
    private readonly List<MethodData> _methods = [];

    private IrCallingConventions(CfgGeneratorContext context) {
        _context = context;
        foreach (CfgCodePartition partition in context.Program.Partitions) {
            foreach (CfgCodePartitionEntry entry in partition.Entries) {
                _entryByNode[entry.Node] = entry;
                _states[entry] = new EntryState();
            }
        }
    }

    /// <summary>How many iterations the last <see cref="Compute"/> ran to the fixed point.</summary>
    internal int Iterations { get; private set; }

    /// <summary>Computes the conventions of <paramref name="methods"/> to a fixed point.</summary>
    public static IrCallingConventions Compute(IReadOnlyList<PreparedMethod> methods,
        CfgGeneratorContext context) {
        IrCallingConventions conventions = new(context);
        conventions.Preprocess(methods);
        conventions.SeedUnknownWays();
        conventions.Iterate();
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

    /// <summary>The stack slots the preprocess computed for <paramref name="methodName"/>;
    /// <see cref="IrStackSlots.Empty"/> when the method has no stack node.</summary>
    internal IrStackSlots SlotsOf(string methodName) {
        if (_methodByName.TryGetValue(methodName, out MethodData? data)) {
            return data.Slots;
        }
        return IrStackSlots.Empty;
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
        public required List<CfgCodePartitionEntry> Targets { get; init; }
        public required bool Resolved { get; init; }
    }

    private enum ExitKind { Return, Abort, TransferResolved, TransferUnresolved }

    private sealed class ExitSite {
        public required IrExit Exit { get; init; }
        public required IrBlock Block { get; init; }
        public required ExitKind Kind { get; init; }
        public required bool Iret { get; init; }
        public List<CfgCodePartitionEntry> Targets { get; init; } = [];
    }

    private sealed class MethodData {
        public required PreparedMethod Source { get; init; }
        public required IrSsa Ssa { get; init; }
        public required IrStackSlots Slots { get; init; }
        public required List<EntrySite> Entries { get; init; }
        public required List<CallSite> Calls { get; init; }
        public required List<ExitSite> Exits { get; init; }
        public required Dictionary<IrStatement, CallSite> CallByStatement { get; init; }
        public required Dictionary<IrExit, ExitSite> ExitByTerminator { get; init; }
        public required Dictionary<int, CfgCodePartitionEntry> EntryByLoadOffset { get; init; }
        public bool Returns { get; set; }
        /// <summary><see langword="true"/> when a state the memos were computed under changed and
        /// they must be cleared before the next <see cref="Needed"/> or <see cref="Preservation"/> call.</summary>
        public bool MemoStale { get; set; }
        public readonly Dictionary<int, CpuValues> NeededMemo = new();
        public required IrPreservation Preservation { get; set; }
    }

    private void Preprocess(IReadOnlyList<PreparedMethod> methods) {
        foreach (PreparedMethod source in methods) {
            IrStackSlots slots = IrStackSlots.Empty;
            if (NeedsStackSlots(source.IrMethod)) {
                slots = IrStackSlots.Compute(source.IrMethod, new IrMemoryAliasAnalysis(source.Ssa));
            }
            _methodByName[source.Method.MethodName] = new MethodData {
                Source = source,
                Ssa = source.Ssa,
                Slots = slots,
                Entries = [],
                Calls = [],
                Exits = [],
                CallByStatement = [],
                ExitByTerminator = [],
                EntryByLoadOffset = [],
                Preservation = CreatePreservation(source, slots)
            };
        }
        foreach (MethodData method in _methodByName.Values) {
            PreprocessMethod(method);
            _methods.Add(method);
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
            foreach (IrStatement statement in block.Statements) {
                if (statement.Node is CheckExternalEventsNode
                    || statement.Origin is not CfgInstruction { IsCall: true } instruction) {
                    continue;
                }
                List<CfgCodePartitionEntry> targets = [];
                bool resolved = true;
                foreach (ICfgNode target in HardwareInterruptEffectBuilder.TransferTargets(instruction)) {
                    if (_entryByNode.TryGetValue(target, out CfgCodePartitionEntry? mapped)) {
                        targets.Add(mapped);
                    } else {
                        resolved = false;
                    }
                }
                resolved &= targets.Count > 0;
                int[] defined = new int[ValueCount];
                Array.Fill(defined, -1);
                foreach ((IrSsaValue value, int version) in ssa.Definitions(statement)) {
                    if ((int)value < ValueCount) {
                        defined[(int)value] = version;
                    }
                }
                CallSite site = new() { DefinedVersions = defined, Targets = targets, Resolved = resolved };
                method.Calls.Add(site);
                method.CallByStatement[statement] = site;
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
            if (block.HasTerminator && block.Terminator is IrExit exit) {
                ExitSite site = ClassifyExit(exit, block);
                method.Exits.Add(site);
                method.ExitByTerminator[exit] = site;
                method.Returns |= site.Kind is ExitKind.Return or ExitKind.TransferResolved;
                if (site.Kind == ExitKind.TransferResolved) {
                    foreach (CfgCodePartitionEntry target in site.Targets) {
                        AddReader(target, method);
                    }
                }
            }
        }
        foreach (EntrySite site in method.Entries) {
            _states[site.Entry].Returns = method.Returns;
        }
    }

    private ExitSite ClassifyExit(IrExit exit, IrBlock block) {
        bool iret = Contains<InterruptReturnNode>(exit.Transfer);
        if (iret || exit.Origin is CfgInstruction { IsReturn: true }) {
            return new ExitSite { Exit = exit, Block = block, Kind = ExitKind.Return, Iret = iret };
        }
        List<CfgCodePartitionEntry> targets = [];
        bool foundTransfer = false;
        bool resolved = true;
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(exit.Transfer)) {
            switch (node) {
                case MethodCallNode { PropertyPath: null } call
                    when call.GetType() == typeof(MethodCallNode): {
                    foundTransfer = true;
                    int? loadOffset = call.Arguments switch {
                        [] => 0,
                        [OffsetLiteralNode literal] => literal.Value,
                        _ => null
                    };
                    if (loadOffset is { } resolvedOffset
                        && _methodByName.TryGetValue(call.MethodName, out MethodData? target)
                        && target.EntryByLoadOffset.TryGetValue(resolvedOffset, out CfgCodePartitionEntry? entry)) {
                        targets.Add(entry);
                    } else {
                        resolved = false;
                    }
                    break;
                }
                case CyclicJumpNode cyclic: {
                    foundTransfer = true;
                    if (_methodByName.TryGetValue(cyclic.MethodName, out MethodData? target)
                        && target.EntryByLoadOffset.TryGetValue(cyclic.LoadOffset, out CfgCodePartitionEntry? entry)) {
                        targets.Add(entry);
                    } else {
                        resolved = false;
                    }
                    break;
                }
            }
        }
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

    private static bool IsReturnExit(IrExit exit) =>
        Contains<InterruptReturnNode>(exit.Transfer)
        || exit.Origin is CfgInstruction { IsReturn: true };

    private static bool Contains<T>(IVisitableAstNode node) where T : IVisitableAstNode =>
        node is T || IrAstChildren.Descendants(node).OfType<T>().Any();

    private static bool NeedsStackSlots(IrMethod method) =>
        method.Blocks.SelectMany(block => block.Statements).Any(statement =>
            statement.Node is StackCallNode
            || IrAstChildren.Descendants(statement.Node).OfType<StackCallValueNode>().Any());

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

    private void Iterate() {
        // Each round recomputes only the entry sites and outputs whose inputs changed: a site when
        // an input, a change or an output it reads moved; an outputs when its entry's changes or a
        // caller's liveness inputs moved. Writes happen in the same positions as a full recompute,
        // and a skipped site reads unchanged inputs and would rewrite its stored value, so the
        // result is identical.
        HashSet<CfgCodePartitionEntry> sitesDirty = new(_methodByEntry.Keys);
        HashSet<CfgCodePartitionEntry> outputsDirty = new(_states.Keys);
        for (int iteration = 0; ; iteration++) {
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
                    foreach (CfgCodePartitionEntry target in exit.Targets) {
                        changes |= _states[target].Changes;
                    }
                    break;
                case ExitKind.TransferUnresolved:
                    changes |= Reportable;
                    break;
            }
        }
        return changes & Reportable;
    }

    /// <summary>What callers read after <paramref name="entry"/>: everything when it has a non-call
    /// way in, else the union over its call sites of the bytes still needed after the call.</summary>
    private CpuValues LiveAfter(CfgCodePartitionEntry entry, EntryState state) {
        if (state.UnknownWay) {
            return Reportable;
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
            return NeededTerminatorUse(method, value, mask, terminator, visiting);
        }
        if (use.Statement is not { } statement) {
            return mask;
        }
        if (method.CallByStatement.TryGetValue(statement, out CallSite? call)) {
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
            CpuValues written = (statement.Effects.MustWrite | statement.Effects.MayWrite) & mask;
            return Needed(method, value, use.ConditionalNewVersion, visiting) & ~written;
        }
        return statement.Effects.Reads & mask;
    }

    private CpuValues NeededTerminatorUse(MethodData method, IrSsaValue value, CpuValues mask,
        IrTerminator terminator, HashSet<int> visiting) {
        if (terminator is not IrExit exit) {
            return terminator.Effects.Reads & mask;
        }
        switch (method.ExitByTerminator[exit].Kind) {
            case ExitKind.Return: {
                CpuValues outputs = CpuValues.None;
                foreach (EntrySite site in method.Entries) {
                    outputs |= _states[site.Entry].Outputs;
                }
                return outputs & mask;
            }
            case ExitKind.Abort:
                return AbortReads(exit.Transfer) & mask;
            case ExitKind.TransferResolved: {
                CpuValues inputs = CpuValues.None;
                foreach (CfgCodePartitionEntry target in method.ExitByTerminator[exit].Targets) {
                    inputs |= _states[target].Inputs;
                }
                return inputs & mask;
            }
            default:
                return mask;
        }
    }

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
            if (!read.Complete || !IrPreservation.IsPopRestore(pop) || !Defines(method.Ssa, pop, value, out int popVersion)) {
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

    private CpuValues TargetsInputs(CallSite call) {
        CpuValues inputs = CpuValues.None;
        foreach (CfgCodePartitionEntry target in call.Targets) {
            inputs |= _states[target].Inputs;
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
