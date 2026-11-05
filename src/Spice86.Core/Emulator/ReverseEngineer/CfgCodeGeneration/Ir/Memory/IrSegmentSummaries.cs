namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The program-wide segment-register summaries: for every generated partition entry, what each of
/// DS, ES, FS and GS provably holds at every way in (a known value or a shared equality with other
/// registers, else unknown) and what its returns leave behind (the value preserved, a known
/// constant, or unknown). The only ways in that carry information are a call edge of the CFG from
/// an instruction in a generated partition and the DOS loader of the program entry; every other way
/// (a jump or fall-through from another partition, an execution context entry, a return target)
/// gives unknown. A callee leaves a register preserved when its write summary reports it not
/// written, or known when every return resolves to the same constant. Entry summaries, exit
/// summaries and call results feed each other, so the computation is a fixed point that runs until
/// nothing changes.
/// <para>Soundness: the ways in of an entry enumerate every way the partition can be entered, so
/// the meet is taken over a superset of the executions that reach it; a "preserved" exit is
/// decided by value (every return computes the value the register held at that entry), so it
/// holds whatever version the SSA happened to name it with. The final state is a post-fixed
/// point: every entry fact and exit fact is consistent with every way in and every return under
/// those same facts, so the claimed values hold inductively along any call chain an execution
/// can take.</para>
/// </summary>
internal sealed class IrSegmentSummaries {
    /// <summary>The registers the summaries track, in canonical order.</summary>
    internal static readonly IrSsaValue[] SegmentRegisters =
        IrValueSets.ValuesIn(CpuValues.Es | CpuValues.Ds | CpuValues.Fs | CpuValues.Gs);

    /// <summary>The values the summaries track: the segment registers first (the equality pairs
    /// cover only them), then the direction flag, whose value lattice is the same without
    /// equalities.</summary>
    internal static readonly IrSsaValue[] TrackedValues =
        [.. SegmentRegisters, IrSsaValue.Direction];
    private const int MaxEqualityIterations = 40;
    private const int WideningIterations = 60;
    private static readonly int RegisterCount = SegmentRegisters.Length;
    private static readonly int ValueCount = TrackedValues.Length;

    private readonly CfgGeneratorContext _context;
    private readonly HardwareInterruptEffectBuilder _effectBuilder;
    private readonly List<MethodData> _methods = [];
    private readonly Dictionary<MethodPlan, IrSegmentValues> _valuesByPlan = new();
    private readonly Dictionary<ICfgNode, (CfgCodePartition Partition, CfgCodePartitionEntry Entry)> _entryByNode = new();
    private readonly Dictionary<CfgCodePartitionEntry, EntryState> _states = new();
    private readonly Dictionary<CfgCodePartitionEntry, bool[]> _preserved = new();
    private readonly Dictionary<CfgCodePartitionEntry, MethodData> _methodByEntry = new();
    private readonly Dictionary<CfgCodePartitionEntry, (MethodData Method, FactEntry FactEntry)> _factEntryByEntry = new();
    private readonly Dictionary<CfgCodePartitionEntry, HashSet<MethodData>> _callersOf = new();
    private bool _equalityEnabled = true;

    /// <summary>How many iterations the last <see cref="Compute"/> ran to the fixed point.</summary>
    internal int Iterations { get; private set; }

    /// <summary>How many entries were pinned to the top by the widening bound.</summary>
    internal int WidenedEntryCount { get; private set; }

    private IrSegmentSummaries(CfgGeneratorContext context, HardwareInterruptEffectBuilder effectBuilder) {
        _context = context;
        _effectBuilder = effectBuilder;
        foreach (CfgCodePartition partition in context.Program.Partitions) {
            foreach (CfgCodePartitionEntry entry in partition.Entries) {
                _entryByNode[entry.Node] = (partition, entry);
                _states[entry] = new EntryState();
            }
        }
    }

    /// <summary>Computes the summaries of <paramref name="methods"/> to a fixed point.
    /// <paramref name="effectBuilder"/> is the effect builder whose per-(partition, entry) write summaries
    /// decide which registers a callee preserves.</summary>
    public static IrSegmentSummaries Compute(IReadOnlyList<PreparedMethod> methods,
        CfgGeneratorContext context, HardwareInterruptEffectBuilder effectBuilder) {
        IrSegmentSummaries summaries = new IrSegmentSummaries(context, effectBuilder);
        summaries.Preprocess(methods);
        summaries.SeedStaticWays();
        summaries.Iterate();
        return summaries;
    }

    /// <summary>What <paramref name="value"/> holds at <paramref name="entry"/>: the constant every
    /// way in gives, else unknown.</summary>
    public SegmentValue EntryValue(CfgCodePartitionEntry entry, IrSsaValue value) {
        if (!_states.TryGetValue(entry, out EntryState? state)) {
            return SegmentValue.Unknown.Instance;
        }
        int index = ValueIndex(value);
        if (state.States[index] != 1) {
            return SegmentValue.Unknown.Instance;
        }
        return new SegmentValue.Known(state.Constants[index]);
    }

    /// <summary>The registers equal at every way in to <paramref name="entry"/>, grouped into
    /// classes of two or more; each class shares one entry root.</summary>
    public IReadOnlyList<IReadOnlySet<IrSsaValue>> EqualityClasses(CfgCodePartitionEntry entry) {
        if (!_states.TryGetValue(entry, out EntryState? state)) {
            return [];
        }
        return Classes(state.EqualityMask)
            .Select(members => (IReadOnlySet<IrSsaValue>)members
                .Select(index => SegmentRegisters[index]).ToHashSet())
            .ToList();
    }

    /// <summary>What a return of <paramref name="entry"/> leaves in <paramref name="value"/>.</summary>
    public IrSegmentExitValue ExitValue(CfgCodePartitionEntry entry, IrSsaValue value) {
        if (!_states.TryGetValue(entry, out EntryState? state)) {
            return IrSegmentExitValue.Unknown.Instance;
        }
        return state.Exits[ValueIndex(value)];
    }

    /// <summary>The analysis of <paramref name="plan"/>'s method computed with the final
    /// facts.</summary>
    public IrSegmentValues ValuesFor(MethodPlan plan) => _valuesByPlan[plan];

    private sealed class MethodData {
        public required PreparedMethod Source { get; init; }
        public required IrSsa Ssa { get; init; }
        public required IrStackSlots Slots { get; init; }
        public required List<CallSite> Calls { get; init; }
        public required List<IrTerminator> Returns { get; init; }
        public required List<FactEntry> FactEntries { get; init; }
        private IrSegmentValues? _values;
        public IrSegmentValues Values =>
            _values ?? throw new InvalidOperationException("Values are computed after construction.");
        public void SetValues(IrSegmentValues values) => _values = values;
        public IrSegmentFacts? Facts { get; set; }
        public bool HasAbortExit { get; set; }
    }

    private sealed record FactEntry(CfgCodePartitionEntry Entry, IrBlock Block, int[] EntryVersions);

    private sealed record CallSite(IrBlock Block, int Index, CfgInstruction Instruction,
        List<(IrSsaValue Value, int Version)> Defined, int[] ReadVersions,
        List<CfgCodePartitionEntry> Targets, bool AllTargetsMapped);

    private readonly record struct Way(SegmentValue[] Values, ushort EqualityMask);



    private sealed class EntryState {
        public readonly List<Way> StaticWays = [];
        public readonly Dictionary<CallSite, Way> CallWays = [];
        public bool UnknownWay;
        public bool Widened;
        public bool Reached;
        public readonly byte[] States = new byte[ValueCount];
        public readonly ushort[] Constants = new ushort[ValueCount];
        public ushort EqualityMask;
        public IrSegmentExitValue[] Exits =
            [.. Enumerable.Repeat(IrSegmentExitValue.Unknown.Instance, ValueCount)];
    }

    private static int ValueIndex(IrSsaValue value) =>
        Array.IndexOf(TrackedValues, value);

    private static ushort PairBit(int first, int second) =>
        (ushort)(1 << (first * RegisterCount + second));

    private void Preprocess(IReadOnlyList<PreparedMethod> methods) {
        foreach (PreparedMethod source in methods) {
            _methods.Add(CollectMethodData(source));
        }
        foreach (MethodData data in _methods) {
            data.SetValues(new IrSegmentValues(data.Source.IrMethod, data.Ssa, data.Slots,
                IrSegmentFacts.None,
                data.Calls.SelectMany(call => call.Defined)
                    .Select(defined => defined.Version).ToHashSet()));
            _valuesByPlan[data.Source.Method] = data.Values;
        }
        SeedLiftedEffects();
        ComputePreservedMasks();
        foreach (MethodData data in _methods) {
            foreach (FactEntry factEntry in data.FactEntries) {
                _methodByEntry[factEntry.Entry] = data;
                _factEntryByEntry[factEntry.Entry] = (data, factEntry);
            }
            foreach (CallSite call in data.Calls) {
                foreach (CfgCodePartitionEntry target in call.Targets) {
                    if (!_callersOf.TryGetValue(target, out HashSet<MethodData>? callers)) {
                        callers = [];
                        _callersOf[target] = callers;
                    }
                    callers.Add(data);
                }
            }
        }
    }

    /// <summary>The calls, returns, aborts and entry-version reads of one method's blocks.</summary>
    private MethodData CollectMethodData(PreparedMethod source) {
        IrSsa ssa = source.Ssa;
        IrStackSlots slots = IrStackSlots.Empty;
        if (NeedsStackSlots(source.IrMethod)) {
            slots = IrStackSlots.Compute(source.IrMethod, new IrMemoryAliasAnalysis(ssa));
        }
        MethodData data = new MethodData {
            Source = source, Ssa = ssa, Slots = slots, Calls = [], Returns = [], FactEntries = []
        };
        foreach (IrEntry entry in source.IrMethod.Entries
                .Where(entry => _entryByNode.ContainsKey(entry.Node))) {
            data.FactEntries.Add(new FactEntry(_entryByNode[entry.Node].Entry, entry.Block,
                TrackedValues.Select(value => ssa.VersionBefore(entry.Block, 0, value))
                    .ToArray()));
        }
        foreach (IrBlock block in source.IrMethod.Blocks) {
            for (int i = 0; i < block.Statements.Count; i++) {
                IrStatement statement = block.Statements[i];
                if (statement.Origin is CfgInstruction { IsCall: true } instruction) {
                    data.Calls.Add(CallSiteOf(block, i, instruction, ssa));
                }
            }
            if (block.HasTerminator && block.Terminator is IrExit exit) {
                if (IsReturnExit(exit)) {
                    data.Returns.Add(exit);
                }
                data.HasAbortExit |= Contains<FailAsUntestedNode>(exit.Transfer)
                    || Contains<ThrowNode>(exit.Transfer);
            }
        }
        return data;
    }

    /// <summary>The call record of one call statement: the segment-register definitions it makes,
    /// the versions it reads and the callee entries its transfer targets map to.</summary>
    private CallSite CallSiteOf(IrBlock block, int index, CfgInstruction instruction, IrSsa ssa) {
        List<CfgCodePartitionEntry> targets = [];
        bool allMapped = true;
        foreach (ICfgNode target in HardwareInterruptEffectBuilder.TransferTargets(instruction)) {
            if (_entryByNode.TryGetValue(target,
                    out (CfgCodePartition Partition, CfgCodePartitionEntry Entry) mapped)) {
                targets.Add(mapped.Entry);
            } else {
                allMapped = false;
            }
        }
        return new CallSite(block, index, instruction,
            ssa.Definitions(block.Statements[index])
                .Where(definition => ValueIndex(definition.Value) >= 0)
                .ToList(),
            TrackedValues.Select(value => ssa.VersionBefore(block, index, value)).ToArray(),
            targets, allMapped);
    }

    /// <summary>Lets the effect builder pre-lift every method whose entries it will summarise.</summary>
    private void SeedLiftedEffects() {
        foreach (MethodData data in _methods) {
            foreach (FactEntry factEntry in data.FactEntries) {
                _effectBuilder.SeedLifted(_entryByNode[factEntry.Entry.Node].Partition,
                    data.Source.IrMethod, data.Ssa, data.Slots);
            }
        }
    }

    /// <summary>The per-entry mask of segment registers the effect writes summary leaves
    /// untouched.</summary>
    private void ComputePreservedMasks() {
        foreach (MethodData data in _methods) {
            foreach (FactEntry factEntry in data.FactEntries) {
                if (_preserved.ContainsKey(factEntry.Entry)) {
                    continue;
                }
                CpuValues writes = _effectBuilder.RegisterWrites(
                    _entryByNode[factEntry.Entry.Node].Partition, factEntry.Entry.Node);
                // The write summary covers registers only: the direction flag's preserved bit is
                // always false and the by-value return resolution decides preservation instead.
                _preserved[factEntry.Entry] = TrackedValues
                    .Select(value => value != IrSsaValue.Direction
                        && (writes & IrSsaValues.MaskOf(value)) == CpuValues.None)
                    .ToArray();
            }
        }
    }

    /// <summary>A method with no stack node records no stack read, so its slots stay empty: the
    /// dataflow run is skipped.</summary>
    private static bool NeedsStackSlots(IrMethod method) =>
        method.Blocks.SelectMany(block => block.Statements).Any(statement =>
            statement.Node is StackCallNode
            || IrAstChildren.Descendants(statement.Node).OfType<StackCallValueNode>().Any());

    private static bool IsReturnExit(IrExit exit) =>
        exit.Transfer is InterruptReturnNode
        || IrAstChildren.Descendants(exit.Transfer).OfType<InterruptReturnNode>().Any()
        || exit.Origin is CfgInstruction { IsReturn: true };

    private static bool Contains<T>(IVisitableAstNode node) where T : IVisitableAstNode =>
        node is T || IrAstChildren.Descendants(node).OfType<T>().Any();

    /// <summary>The ways in that do not depend on the analysis: the DOS loader of the program
    /// entry, and the unknown of every other non-call way.</summary>
    private void SeedStaticWays() {
        foreach ((_, (CfgCodePartition _, CfgCodePartitionEntry entry)) in _entryByNode) {
            EntryState state = _states[entry];
            if (entry.Address.Equals(_context.Program.Inputs.ProgramEntryPoint)) {
                if (LoaderWay(entry) is { } loader) {
                    state.StaticWays.Add(loader);
                } else {
                    state.UnknownWay = true;
                }
            } else if (entry.Kind == CfgCodePartitionEntryKind.ExecutionContextEntry) {
                state.UnknownWay = true;
            }
        }
        foreach (CfgCodePartitionTransfer transfer in _context.Program.Transfers
                .Where(transfer => transfer.Kind != CfgCodePartitionTransferKind.CallOut
                    && _entryByNode.ContainsKey(transfer.TargetNode))) {
            _states[_entryByNode[transfer.TargetNode].Entry].UnknownWay = true;
        }
    }

    /// <summary>The DOS loader state at the program entry: DS = ES = the PSP segment of the loaded
    /// image holding the entry point. A non-DOS image (a raw binary) gives no loader way.</summary>
    private Way? LoaderWay(CfgCodePartitionEntry entry) {
        ProgramImages images = _context.Program.Inputs.ProgramImages;
        uint physical = entry.Address.Linear;
        if (images.Images.FirstOrDefault(image => physical >= image.StartAddress
                && physical < image.StartAddress + image.SizeInBytes) is not { } image) {
            return null;
        }
        SegmentValue[] values =
            [.. Enumerable.Repeat(SegmentValue.Unknown.Instance, ValueCount)];
        int ds = ValueIndex(IrSsaValue.Ds);
        int es = ValueIndex(IrSsaValue.Es);
        values[ds] = values[es] = new SegmentValue.Known(image.FirstSegment);
        return new Way(values, PairBit(Math.Min(ds, es), Math.Max(ds, es)));
    }

    /// <summary>Iterates entry meets and exit updates until nothing changes. After
    /// <see cref="WideningIterations"/>, an entry that changes is pinned to the top, so each
    /// entry changes at most once more and the loop ends.</summary>
    private void Iterate() {
        // Each round recomputes only the items whose inputs changed: a method's facts when one of
        // its entries or a call target's exits moved, an entry's call ways when a caller's values
        // moved, an entry meet when one of its ways moved or it was widened, an exit when its
        // method's values moved or its entry was widened. A skipped item reads unchanged inputs and
        // would reproduce its stored value, so the result is identical to a full recompute.
        HashSet<MethodData> factsDirty = new(_methods);
        HashSet<EntryState> meetDirty = new(_states.Values);
        HashSet<CfgCodePartitionEntry> exitsDirty = new(_factEntryByEntry.Keys);
        for (int iteration = 0; ; iteration++) {
            HashSet<MethodData> valuesChanged = [];
            foreach (MethodData method in _methods) {
                if (!factsDirty.Remove(method)) {
                    continue;
                }
                IrSegmentFacts facts = FactsOf(method);
                if (!facts.ContentEquals(method.Facts)) {
                    method.Values.SetFacts(facts);
                    method.Facts = facts;
                    valuesChanged.Add(method);
                }
            }
            foreach (MethodData method in valuesChanged) {
                UpdateCallWays(method, meetDirty);
                foreach (FactEntry factEntry in method.FactEntries) {
                    exitsDirty.Add(factEntry.Entry);
                }
            }
            bool changed = false;
            foreach ((CfgCodePartitionEntry entry, EntryState state) in _states) {
                if (!meetDirty.Remove(state)) {
                    continue;
                }
                bool met = MeetEntry(state);
                changed |= met;
                if (met && _methodByEntry.TryGetValue(entry, out MethodData? owner)) {
                    factsDirty.Add(owner);
                }
                if (met && iteration >= WideningIterations) {
                    SetWidened(entry, meetDirty, exitsDirty);
                }
            }
            foreach (MethodData method in _methods) {
                foreach (FactEntry factEntry in method.FactEntries) {
                    if (!exitsDirty.Remove(factEntry.Entry)) {
                        continue;
                    }
                    bool updated = UpdateExits(method, factEntry);
                    changed |= updated;
                    if (updated) {
                        if (_callersOf.TryGetValue(factEntry.Entry,
                                out HashSet<MethodData>? callers)) {
                            factsDirty.UnionWith(callers);
                        }
                        if (iteration >= WideningIterations) {
                            SetWidened(factEntry.Entry, meetDirty, exitsDirty);
                        }
                    }
                }
            }
            if (!changed) {
                Iterations = iteration + 1;
                WidenedEntryCount = _states.Values.Count(state => state.Widened);
                return;
            }
            if (iteration >= MaxEqualityIterations && _equalityEnabled) {
                // A degenerate program could keep reshaping equality classes; dropping them keeps
                // the value fix point monotone and sound.
                _equalityEnabled = false;
                foreach (EntryState state in _states.Values) {
                    state.EqualityMask = 0;
                }
                factsDirty.UnionWith(_methods);
                meetDirty.UnionWith(_states.Values);
                exitsDirty.UnionWith(_factEntryByEntry.Keys);
            }
        }
    }

    /// <summary>Pins <paramref name="entry"/> to the top and schedules the re-meet and exit
    /// recompute the new <see cref="EntryState.Widened"/> input implies.</summary>
    private void SetWidened(CfgCodePartitionEntry entry, HashSet<EntryState> meetDirty,
        HashSet<CfgCodePartitionEntry> exitsDirty) {
        EntryState state = _states[entry];
        if (state.Widened) {
            return;
        }
        state.Widened = true;
        meetDirty.Add(state);
        if (_factEntryByEntry.ContainsKey(entry)) {
            exitsDirty.Add(entry);
        }
    }

    /// <summary>Recomputes <paramref name="method"/>'s call ways and replaces them in the targets'
    /// way sets, marking for a re-meet every target whose inputs actually moved.</summary>
    private void UpdateCallWays(MethodData method, HashSet<EntryState> meetDirty) {
        foreach (CallSite call in method.Calls) {
            if (call.Targets.Count == 0) {
                continue;
            }
            Way way = CallWay(method, call);
            foreach (EntryState state in call.Targets.Select(entry => _states[entry])
                .Where(state => CallWayChanged(state, call, way))) {
                state.CallWays[call] = way;
                meetDirty.Add(state);
            }
        }
    }

    private static bool CallWayChanged(EntryState state, CallSite call, Way way) =>
        !state.CallWays.TryGetValue(call, out Way old) || !SameWay(old, way);

    private static bool SameWay(Way first, Way second) =>
        first.EqualityMask == second.EqualityMask
        && first.Values.SequenceEqual(second.Values);

    /// <summary>What the caller proves about every tracked value just before
    /// <paramref name="call"/>: the resolved values, and the segment-register pairs equal
    /// there.</summary>
    private Way CallWay(MethodData method, CallSite call) {
        SegmentValue[] segmentValues = new SegmentValue[ValueCount];
        for (int i = 0; i < ValueCount; i++) {
            segmentValues[i] = method.Values.ValueOf(call.ReadVersions[i]);
        }
        ushort equalityMask = 0;
        if (_equalityEnabled) {
            for (int i = 0; i < RegisterCount; i++) {
                for (int j = i + 1; j < RegisterCount; j++) {
                    if (Same(segmentValues[i], segmentValues[j])) {
                        equalityMask |= PairBit(i, j);
                    }
                }
            }
        }
        return new Way(segmentValues, equalityMask);
    }

    private static bool Same(SegmentValue first, SegmentValue second) => (first, second) switch {
        (SegmentValue.Known a, SegmentValue.Known b) => a.Value == b.Value,
        (SegmentValue.Symbolic a, SegmentValue.Symbolic b) => a.RootVersion == b.RootVersion,
        _ => false
    };

    /// <summary>Folds every way of <paramref name="state"/> into its entry values and equalities;
    /// a non-call way makes everything unknown.</summary>
    private static bool MeetEntry(EntryState state) {
        byte[] states = new byte[ValueCount];
        ushort[] constants = new ushort[ValueCount];
        ushort equalityMask = 0;
        bool reached = false;
        foreach (Way way in state.StaticWays.Concat(state.CallWays.Values)) {
            equalityMask = reached ? (ushort)(equalityMask & way.EqualityMask) : way.EqualityMask;
            reached = true;
            for (int i = 0; i < ValueCount; i++) {
                bool known = way.Values[i] is SegmentValue.Known;
                ushort constant = way.Values[i] is SegmentValue.Known k ? k.Value : (ushort)0;
                byte wayState = known ? (byte)1 : (byte)2;
                if (states[i] == 0) {
                    states[i] = wayState;
                    constants[i] = constant;
                } else if (states[i] != wayState || (known && constants[i] != constant)) {
                    states[i] = 2;
                }
            }
        }
        if (state.UnknownWay || state.Widened) {
            reached = true;
            equalityMask = 0;
            Array.Fill(states, (byte)2);
        }
        for (int i = 0; i < ValueCount; i++) {
            if (states[i] != 1) {
                constants[i] = 0;
            }
        }
        bool changed = reached != state.Reached
            || !states.SequenceEqual(state.States)
            || !constants.SequenceEqual(state.Constants)
            || equalityMask != state.EqualityMask;
        state.Reached = reached;
        states.CopyTo(state.States, 0);
        constants.CopyTo(state.Constants, 0);
        state.EqualityMask = equalityMask;
        return changed;
    }

    /// <summary>A register is preserved when its write summary leaves it untouched, or when
    /// every return resolves to the value it had at this entry; it is known when every return
    /// resolves to the same constant (counting a return of the entry value at a known entry).
    /// A method holding an abort exit (an untested instruction or a fault) may do anything before
    /// it resumes, so its exits are unknown.</summary>
    private bool UpdateExits(MethodData method, FactEntry factEntry) {
        EntryState state = _states[factEntry.Entry];
        bool[] preserved = _preserved[factEntry.Entry];
        IrSegmentValues values = method.Values;
        IrSegmentExitValue[] next = new IrSegmentExitValue[ValueCount];
        for (int i = 0; i < ValueCount; i++) {
            next[i] = ExitOf(method, values, factEntry.EntryVersions[i], preserved[i],
                state.Widened, i);
        }
        bool changed = !next.SequenceEqual(state.Exits);
        state.Exits = next;
        return changed;
    }

    private IrSegmentExitValue ExitOf(MethodData method, IrSegmentValues values, int entryVersion,
        bool preserved, bool widened, int register) {
        if (widened || method.HasAbortExit) {
            return IrSegmentExitValue.Unknown.Instance;
        }
        if (preserved) {
            return IrSegmentExitValue.Preserved.Instance;
        }
        SegmentValue entry = values.ValueOf(entryVersion);
        SegmentValue? merged = null;
        foreach (IrTerminator terminator in method.Returns) {
            SegmentValue value = values.ValueAt(terminator, TrackedValues[register]);
            if (IsEntryValue(values, entry, value)) {
                value = entry;
            } else if (value is not SegmentValue.Known) {
                return IrSegmentExitValue.Unknown.Instance;
            }
            if (merged is null) {
                merged = value;
            } else if (!merged.Equals(value)) {
                return IrSegmentExitValue.Unknown.Instance;
            }
        }
        if (merged is null || merged is not SegmentValue.Known and not SegmentValue.Symbolic) {
            return IrSegmentExitValue.Unknown.Instance;
        }
        if (merged.Equals(entry)) {
            return IrSegmentExitValue.Preserved.Instance;
        }
        if (merged is not SegmentValue.Known known) {
            return IrSegmentExitValue.Unknown.Instance;
        }
        return new IrSegmentExitValue.Known(known.Value);
    }

    /// <summary>Whether <paramref name="returnValue"/> provably is the value the register held at
    /// the entry: the entry version's resolved value, or a symbolic whose own resolution equals
    /// it (a non-<see cref="SegmentValue.Unknown"/> one). The resolved comparison keeps this true
    /// when an equality class makes a restored register resolve to a different member's entry
    /// version -- the value at entry is the same whichever class member named it.</summary>
    private static bool IsEntryValue(IrSegmentValues values, SegmentValue entry,
        SegmentValue returnValue) {
        if (returnValue.Equals(entry)) {
            return true;
        }
        return returnValue is SegmentValue.Symbolic symbolic
            && values.ValueOf(symbolic.RootVersion) is { } resolved
            && resolved is not SegmentValue.Unknown
            && resolved.Equals(entry);
    }

    private IrSegmentFacts FactsOf(MethodData method) {
        IrSegmentFacts facts = new();
        foreach (FactEntry factEntry in method.FactEntries) {
            EntryState state = _states[factEntry.Entry];
            if (state.Reached) {
                facts.SetEntry(factEntry.Block, EntryFact(state));
            }
        }
        foreach (CallSite call in method.Calls) {
            foreach ((IrSsaValue value, int version) in call.Defined) {
                if (CallResult(call, value) is { } result) {
                    facts.SetCall(version, result);
                }
            }
        }
        return facts;
    }

    /// <summary>The fact of a reached entry: known constants first, then for each equality class
    /// the root is the entry version of its first member in canonical order.</summary>
    private IrSegmentEntryFact EntryFact(EntryState state) {
        Dictionary<IrSsaValue, ushort> known = new();
        Dictionary<IrSsaValue, IrSsaValue> equalTo = new();
        for (int i = 0; i < ValueCount; i++) {
            if (state.States[i] == 1) {
                known[TrackedValues[i]] = state.Constants[i];
            }
        }
        foreach (List<int> members in Classes(state.EqualityMask)) {
            IrSsaValue canonical = SegmentRegisters[members[0]];
            foreach (int member in members) {
                equalTo[SegmentRegisters[member]] = canonical;
            }
        }
        return new IrSegmentEntryFact(known, equalTo);
    }

    /// <summary>The equality classes of <paramref name="equalityMask"/>: groups of at least two
    /// registers linked by equal pairs, members in canonical order.</summary>
    private static List<List<int>> Classes(ushort equalityMask) {
        int[] parent = [.. Enumerable.Range(0, RegisterCount)];
        int Find(int index) {
            while (parent[index] != index) {
                index = parent[index];
            }
            return index;
        }
        for (int i = 0; i < RegisterCount; i++) {
            for (int j = i + 1; j < RegisterCount; j++) {
                if ((equalityMask & PairBit(i, j)) != 0) {
                    parent[Math.Max(Find(i), Find(j))] = Math.Min(Find(i), Find(j));
                }
            }
        }
        return Enumerable.Range(0, RegisterCount)
            .GroupBy(Find)
            .Where(group => group.Count() > 1)
            .Select(group => group.OrderBy(index => index).ToList())
            .ToList();
    }

    /// <summary>What <paramref name="call"/> leaves in <paramref name="value"/>: the read version
    /// when every observed callee entry preserves it, the constant when every one returns the same,
    /// else <see langword="null"/> (unresolved or unanalysable target, conflicting results). An
    /// <c>int</c> site never gets a call result from its handler's summary: a software service may
    /// return a pointer in segment registers (DOS calls answer in ES:BX or DS:BX), which the callee
    /// summaries do not model. The typed lowering of <see cref="IrInterruptServiceCalls"/> is what
    /// carries the register rule instead: an untyped <c>int</c> statement defines every register, so
    /// nothing survives it, while a typed service call defines only its contract's
    /// <see cref="ServiceContract.Registers"/>' writes, so a segment register outside them keeps the caller's
    /// value across the call.</summary>
    private IrSegmentCallValue? CallResult(CallSite call, IrSsaValue value) {
        if (!call.AllTargetsMapped
            || HardwareInterruptEffectBuilder.IsInterruptCall(call.Instruction)) {
            return null;
        }
        IrSegmentExitValue[] exits = call.Targets
            .Select(entry => _states[entry].Exits[ValueIndex(value)]).Distinct().ToArray();
        if (exits is not [IrSegmentExitValue result]) {
            return null;
        }
        return result switch {
            IrSegmentExitValue.Preserved => new IrSegmentCallValue.SameAsRead(
                call.ReadVersions[ValueIndex(value)]),
            IrSegmentExitValue.Known known => new IrSegmentCallValue.Known(known.Value),
            _ => null
        };
    }
}
