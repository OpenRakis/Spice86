namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Buffers.Binary;
using System.Linq;

/// <summary>
/// Computes the per-program hardware interrupt effect: the registers a hardware
/// interrupt handler delivered at a <c>CheckExternalEvents</c> call may read from the interrupted
/// code (<see cref="CallEffect.Reads"/>) and may leave changed at its <c>iret</c>
/// (<see cref="CallEffect.Writes"/>). The handler roots are the partitions entered
/// through an <see cref="CfgCodePartitionEntryKind.ExecutionContextEntry"/> other than the program
/// start; each is summarized from its lifted IR (pass pipeline up to the first SSA build, with the
/// conservative checks inside). A register survives when its version at the exit still holds the
/// entry value by <see cref="IrPreservation"/>, shared with the calling conventions: the entry
/// version, a pop of bytes a push took from a preserved version of the same register, or the
/// previous bytes a followed call or <c>int</c>'s targets do not write. Anything the analysis
/// cannot follow - an unknown effect, an unknown call or jump target, a cycle that does not converge,
/// or a hardware vector whose IVT entry is not a known handler - yields the conservative set. A call or jump target
/// inside the analysed partition is an internal edge of the method, not a transfer. The int 74h
/// stub far-calls the mouse driver, which far-calls the user subroutine the program registered:
/// that program code is not followed, so when a subroutine was registered during the run the
/// registers the driver does not restore join Writes and the ones it does not load join Uses.
/// </summary>
internal sealed class HardwareInterruptEffectBuilder {
    /// <summary>What the mouse driver does not restore after its far call to the user subroutine:
    /// FS, GS and the upper halves of the saved 32-bit registers.</summary>
    private const CpuValues MouseUserSubroutineWrites = CpuValues.Fs | CpuValues.Gs
        | CpuValues.EaxHigh | CpuValues.EcxHigh | CpuValues.EdxHigh | CpuValues.EbxHigh
        | CpuValues.EbpHigh | CpuValues.EsiHigh | CpuValues.EdiHigh;

    /// <summary>What the mouse driver does not load before its far call to the user subroutine:
    /// every handler register it does not set to the mouse state.</summary>
    private const CpuValues MouseUserSubroutineUses = InterruptHandlerRegisters
        & ~(CpuValues.Ax | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx | CpuValues.Si | CpuValues.Di);

    /// <summary>The registers a hardware interrupt handler can observe and clobber: every general and
    /// segment register except SP/ESP and SS (its iret restores the stack pointer). No status flag or
    /// DF: the iret restores FLAGS, so only a handler that writes the pushed image dirties them -
    /// <see cref="DeliveredFlags"/> covers that case.</summary>
    public const CpuValues InterruptHandlerRegisters = CpuValues.Eax | CpuValues.Ecx
        | CpuValues.Edx | CpuValues.Ebx | CpuValues.Ebp | CpuValues.Esi
        | CpuValues.Edi | CpuValues.Es | CpuValues.Cs | CpuValues.Ds
        | CpuValues.Fs | CpuValues.Gs;

    /// <summary>The status flags and DF a handler can observe when it reads the FLAGS image the
    /// delivery pushed or the flags it was entered with, and can dirty when it writes that image;
    /// a handler that cannot observe them leaves the interrupted code's flag work dead, and one
    /// that cannot patch them leaves its flags unchanged.</summary>
    private const CpuValues DeliveredFlags = CpuValues.StatusFlags | CpuValues.Direction;

    /// <summary>The byte offset of the FLAGS word inside the 16-bit frame a delivery pushes:
    /// IP at +0, CS at +2, FLAGS at +4.</summary>
    private const int DeliveredFlagsOffset = 4;

    /// <summary>The effect to assume when the program's hardware interrupt handlers are unknown:
    /// every register a handler can observe, the delivery frame and the FLAGS image may be read,
    /// and every register a handler can clobber plus the FLAGS image it may patch may be changed.</summary>
    internal static CallEffect Conservative { get; } =
        new(InterruptHandlerRegisters | IrEffectsVisitor.InterruptFrameReads | DeliveredFlags,
            InterruptHandlerRegisters | DeliveredFlags);

    private static CallEffect Empty { get; } = new(CpuValues.None, CpuValues.None);

    private const int MaxSummaryRounds = 64;

    private static readonly IrSsaValue[] SsaRegisters = IrValueSets.ValuesIn(CpuValues.Registers);
    private static readonly IrSsaValue[] SsaValues =
        IrValueSets.ValuesIn(CpuValues.Registers | DeliveredFlags);

    private readonly CfgGeneratorContext _context;
    private readonly TransferEmitter _transferEmitter;
    private readonly AstInstructionRenderer _assemblyRenderer;
    private IrLifter _lifter;
    private readonly GenerationPlan _plan;
    private readonly Dictionary<CfgBlock, CfgCodePartition> _partitionByBlock = new();
    private readonly Dictionary<CfgCodePartition, MethodPlan> _methodByPartition = new();
    private readonly Dictionary<(CfgCodePartition Partition, ICfgNode Entry), CallEffect> _summaries = new();
    private readonly Dictionary<(CfgCodePartition Partition, ICfgNode Entry, int Low, int High,
            bool Writes), bool> _frameRangeAccesses = new();
    private readonly HashSet<(CfgCodePartition, ICfgNode, int, int, bool)> _decidingFrameRanges =
        new();
    private readonly Dictionary<MethodPlan, IrFrames> _framesByMethod = new();
    private readonly Dictionary<MethodPlan, (IrMethod Method, IrSsa Ssa, IrStackSlots Slots)>
        _liftedByMethod = new();
    private readonly Dictionary<(CfgCodePartition Partition, ICfgNode Entry), CallEffect> _workingSummaries = new();
    private readonly HashSet<(CfgCodePartition Partition, ICfgNode Entry)> _activeScc = new();
    private readonly HashSet<(CfgCodePartition Partition, ICfgNode Entry)> _unfinishedSummaries = new();
    private readonly HashSet<SegmentedAddress> _installed = new();
    private readonly HashSet<SegmentedAddress> _stubIntTargets = new();
    private bool _initialized;
    private CpuValues _eventCheckWrites = CpuValues.None;

    /// <summary>Creates the builder over the generation's context and plan. Its lifter uses a
    /// neutral lowerer: while the effect is being computed, the event checks inside the lifted
    /// handlers must not read or clobber the registers the effect is about.</summary>
    public HardwareInterruptEffectBuilder(CfgGeneratorContext context,
        TransferEmitter transferEmitter, AstInstructionRenderer assemblyRenderer,
        GenerationPlan plan) {
        _context = context;
        _transferEmitter = transferEmitter;
        _assemblyRenderer = assemblyRenderer;
        _lifter = new IrLifter(context, new InstructionLowerer(context, transferEmitter, this, Empty),
            assemblyRenderer);
        _plan = plan;
    }

    /// <summary>Computes the program's effect, or <see cref="Conservative"/>
    /// when a hardware vector's IVT target or a chained vector's target is not a known
    /// handler.</summary>
    public CallEffect Compute() {
        EnsureInitialized();
        try {
            CallEffect effect = ComputeEffect();
            // The summaries the effect itself needed were computed while the event-check writes
            // were still neutral: the summaries the callers query afterwards (through
            // RegisterWrites, CallTargetWrites and ProgramHandlerOf) recompute under the effect
            // just returned, whose Writes the event checks then clobber.
            _eventCheckWrites = effect.Writes;
            _lifter = new IrLifter(_context,
                new InstructionLowerer(_context, _transferEmitter, this, effect), _assemblyRenderer);
            return effect;
        } finally {
            _summaries.Clear();
            _frameRangeAccesses.Clear();
            _decidingFrameRanges.Clear();
            _framesByMethod.Clear();
            _workingSummaries.Clear();
            _activeScc.Clear();
            _unfinishedSummaries.Clear();
        }
    }

    private CallEffect ComputeEffect() {
        HardwareInterruptSetup setup = _context.Program.Inputs.HardwareInterrupts;
        List<(CfgCodePartition Partition, CfgCodePartitionEntry Entry)> roots = _context.Program.Partitions
            .SelectMany(partition => partition.Entries
                .Where(entry => entry.Kind == CfgCodePartitionEntryKind.ExecutionContextEntry
                    && !entry.Address.Equals(_context.Program.Inputs.ProgramEntryPoint))
                .Select(entry => (partition, entry)))
            .ToList();
        HashSet<SegmentedAddress> handlerEntries = roots.Select(root => root.Entry.Address).ToHashSet();
        foreach (byte vector in setup.HardwareVectorNumbers) {
            if (!setup.HardwareVectorTargets.TryGetValue(vector, out SegmentedAddress target)
                || (target != SegmentedAddress.ZERO
                    && !handlerEntries.Contains(target) && !_installed.Contains(target))) {
                return Conservative;
            }
        }
        foreach (SegmentedAddress target in setup.ChainedVectorTargets.Values) {
            if (target != SegmentedAddress.ZERO && !_installed.Contains(target)
                && !_stubIntTargets.Contains(target)) {
                return Conservative;
            }
        }

        CpuValues reads = CpuValues.None;
        CpuValues writes = CpuValues.None;
        foreach ((CfgCodePartition partition, CfgCodePartitionEntry entry) in roots) {
            CallEffect summary = SummaryOf(partition, entry.Node);
            reads |= summary.Reads;
            writes |= summary.Writes;
            if (ObservesDeliveredFlags(partition, entry)) {
                reads |= DeliveredFlags;
            }
            if (PatchesDeliveredFlags(partition, entry)) {
                writes |= DeliveredFlags;
            }
        }
        if (setup.MouseUserSubroutineRegistered) {
            reads |= MouseUserSubroutineUses;
            writes |= MouseUserSubroutineWrites;
        }
        return new CallEffect(
            (reads & (InterruptHandlerRegisters | DeliveredFlags))
                | IrEffectsVisitor.InterruptFrameReads,
            writes & (InterruptHandlerRegisters | DeliveredFlags));
    }

    /// <summary>The registers the summary of the (<paramref name="partition"/>,
    /// <paramref name="entry"/>) pair reports as possibly written: the partition summary is not
    /// limited to handler roots.</summary>
    internal CpuValues RegisterWrites(CfgCodePartition partition, ICfgNode entry) {
        EnsureInitialized();
        return SummaryOf(partition, entry).Writes;
    }

    /// <summary>The union of every register every observed target of <paramref name="call"/>
    /// writes, or <see cref="CpuValues.Registers"/> when the call has no observed target or one of
    /// them is not the entry of a program partition.</summary>
    internal CpuValues CallTargetWrites(CfgInstruction call) {
        EnsureInitialized();
        CpuValues writes = CpuValues.None;
        bool hasTarget = false;
        foreach (ICfgNode target in TransferTargets(call)) {
            if (TargetOf(target) is not { } followed || IsInstalled(target)) {
                return CpuValues.Registers;
            }
            hasTarget = true;
            writes |= SummaryOf(followed.Partition, followed.Entry.Node).Writes;
        }
        return hasTarget ? writes : CpuValues.Registers;
    }

    /// <summary>The program partition entry <paramref name="instruction"/> interrupts to, with the
    /// summary of that entry: the <c>int</c> lowers as a direct call to the handler when it has
    /// exactly one observed target that is a partition entry and not an installed Spice86 handler.
    /// <c>null</c> for every other shape; the <c>int</c> keeps the generic vector lowering.</summary>
    internal (CfgCodePartition Partition, CfgCodePartitionEntry Entry, CallEffect Effect)?
        ProgramHandlerOf(CfgInstruction instruction) {
        EnsureInitialized();
        using IEnumerator<ICfgNode> targets = TransferTargets(instruction).GetEnumerator();
        if (!targets.MoveNext()) {
            return null;
        }
        ICfgNode target = targets.Current;
        if (targets.MoveNext() || IsInstalled(target) || TargetOf(target) is not { } followed) {
            return null;
        }
        CallEffect summary = SummaryOf(followed.Partition, followed.Entry.Node);
        return (followed.Partition, followed.Entry, summary);
    }

    /// <summary>Offers the method lifted for generation, its SSA and its stack slots to the
    /// summaries of <paramref name="partition"/>: reused instead of lifting the partition again.
    /// The summary reads the effect's Writes from the context at each event check, so the
    /// effect the method was lifted with does not change the result. An already cached lift (a
    /// neutrally lifted method) is kept.</summary>
    internal void SeedLifted(CfgCodePartition partition, IrMethod method, IrSsa ssa,
        IrStackSlots slots) {
        EnsureInitialized();
        if (_methodByPartition.TryGetValue(partition, out MethodPlan? methodPlan)) {
            _liftedByMethod.TryAdd(methodPlan, (method, ssa, slots));
        }
    }

    private void EnsureInitialized() {
        if (_initialized) {
            return;
        }
        _initialized = true;
        HardwareInterruptSetup interrupts = _context.Program.Inputs.HardwareInterrupts;
        _installed.UnionWith(interrupts.InstalledInterruptHandlerAddresses);
        HashSet<SegmentedAddress> hardwareStubs =
            [.. interrupts.HardwareVectorTargets.Values.Where(_installed.Contains)];
        foreach (CfgCodePartition partition in _context.Program.Partitions) {
            foreach (CfgBlock block in partition.Blocks) {
                _partitionByBlock[block] = partition;
            }
            CollectStubIntTargets(partition, hardwareStubs);
        }
        foreach (MethodPlan method in _plan.Methods) {
            _methodByPartition[method.Partition] = method;
        }
    }

    private CallEffect SummaryOf(CfgCodePartition partition, ICfgNode entry) {
        (CfgCodePartition Partition, ICfgNode Entry) key = (partition, entry);
        if (_summaries.TryGetValue(key, out CallEffect? cached)) {
            return cached;
        }
        if (_workingSummaries.TryGetValue(key, out CallEffect? working)) {
            return working;
        }
        if (_activeScc.Contains(key)) {
            SegmentedAddress active = _activeScc.FirstOrDefault().Entry?.Address ?? entry.Address;
            throw new InvalidOperationException(
                $"Summary dependency misses {entry.Address}: it belongs to the active SCC of {active} but has no working summary.");
        }
        if (_unfinishedSummaries.Contains(key)) {
            SegmentedAddress active = _activeScc.FirstOrDefault().Entry?.Address ?? entry.Address;
            throw new InvalidOperationException(
                $"Summary dependency misses {entry.Address}: it belongs to an unfinished SCC while {active} is being computed.");
        }
        foreach (List<(CfgCodePartition Partition, ICfgNode Entry)> scc in StronglyConnectedComponents(key)) {
            ComputeScc(scc);
        }
        return _summaries[key];
    }

    /// <summary>Recomputes one strongly connected component from empty summaries until its member
    /// effects stop growing; a member that still changes after the cap falls back to conservative.</summary>
    private void ComputeScc(List<(CfgCodePartition Partition, ICfgNode Entry)> members) {
        _activeScc.UnionWith(members);
        foreach ((CfgCodePartition Partition, ICfgNode Entry) member in members) {
            _workingSummaries[member] = Empty;
        }
        bool converged = false;
        try {
            for (int round = 0; round < MaxSummaryRounds; round++) {
                bool changed = false;
                foreach ((CfgCodePartition partition, ICfgNode entry) in members) {
                    (CfgCodePartition Partition, ICfgNode Entry) member = (partition, entry);
                    CallEffect previous = _workingSummaries[member];
                    CfgCodePartitionEntry? entryMeta = partition.Entries.FirstOrDefault(e => Equals(e.Node, entry));
                    CallEffect next = EntrySummary(partition, entry, entryMeta);
                    if ((previous.Reads & ~next.Reads) != CpuValues.None
                        || (previous.Writes & ~next.Writes) != CpuValues.None) {
                        throw new InvalidOperationException(
                            $"Summary of {entry.Address} lost bits between fixed-point rounds: the iteration is not monotone.");
                    }
                    if (!next.Equals(previous)) {
                        _workingSummaries[member] = next;
                        changed = true;
                    }
                }
                if (!changed) {
                    converged = true;
                    break;
                }
            }
            foreach ((CfgCodePartition Partition, ICfgNode Entry) member in members) {
                _summaries[member] = converged ? _workingSummaries[member] : Conservative;
                _unfinishedSummaries.Remove(member);
            }
        } finally {
            foreach ((CfgCodePartition Partition, ICfgNode Entry) member in members) {
                _activeScc.Remove(member);
                _workingSummaries.Remove(member);
            }
        }
    }

    /// <summary>Tarjan's strongly connected components of the unresolved summary graph reachable
    /// from <paramref name="root"/>, emitted callees first so every dependency is stored before use.</summary>
    private List<List<(CfgCodePartition Partition, ICfgNode Entry)>> StronglyConnectedComponents(
        (CfgCodePartition Partition, ICfgNode Entry) root) {
        Dictionary<(CfgCodePartition Partition, ICfgNode Entry), int> indices = new();
        Dictionary<(CfgCodePartition Partition, ICfgNode Entry), int> lowLinks = new();
        Stack<(CfgCodePartition Partition, ICfgNode Entry)> stack = new();
        HashSet<(CfgCodePartition Partition, ICfgNode Entry)> onStack = new();
        List<List<(CfgCodePartition Partition, ICfgNode Entry)>> components = [];
        int nextIndex = 0;

        Visit(root);
        _unfinishedSummaries.UnionWith(indices.Keys);
        return components;

        void Visit((CfgCodePartition Partition, ICfgNode Entry) key) {
            indices[key] = nextIndex;
            lowLinks[key] = nextIndex;
            nextIndex++;
            stack.Push(key);
            onStack.Add(key);
            foreach ((CfgCodePartition Partition, ICfgNode Entry) dependency in
                     SummaryDependencies(key.Partition, key.Entry)) {
                if (_summaries.ContainsKey(dependency)) {
                    continue;
                }
                if (!indices.ContainsKey(dependency)) {
                    Visit(dependency);
                    lowLinks[key] = Math.Min(lowLinks[key], lowLinks[dependency]);
                } else if (onStack.Contains(dependency)) {
                    lowLinks[key] = Math.Min(lowLinks[key], indices[dependency]);
                }
            }
            if (lowLinks[key] != indices[key]) {
                return;
            }
            List<(CfgCodePartition Partition, ICfgNode Entry)> component = [];
            (CfgCodePartition Partition, ICfgNode Entry) member;
            do {
                member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
            } while (!member.Equals(key));
            components.Add(component);
        }
    }

    /// <summary>The summary keys <see cref="EntrySummary"/> can request for
    /// (<paramref name="partition"/>, <paramref name="entry"/>): the same followed targets the
    /// transfer rules use, before any effect is computed.</summary>
    private IEnumerable<(CfgCodePartition Partition, ICfgNode Entry)> SummaryDependencies(
        CfgCodePartition partition, ICfgNode entry) {
        CfgCodePartitionEntry? entryMeta = partition.Entries.FirstOrDefault(e => Equals(e.Node, entry));
        if (entryMeta is not null && _installed.Contains(entryMeta.Address)) {
            foreach (CfgInstruction instruction in partition.Blocks
                         .SelectMany(block => block.Instructions)
                         .OfType<CfgInstruction>()) {
                if (!IsInterruptCall(instruction)) {
                    continue;
                }
                foreach (ICfgNode target in TransferTargets(instruction)) {
                    if (TargetOf(target) is { } followed
                        && !_installed.Contains(followed.Entry.Address)) {
                        yield return (followed.Partition, followed.Entry.Node);
                    }
                }
            }
            yield break;
        }
        foreach (CfgInstruction instruction in partition.Blocks
                     .SelectMany(block => block.Instructions)
                     .OfType<CfgInstruction>()
                     .Where(instruction => !instruction.IsReturn)) {
            foreach (ICfgNode target in FollowedTargets(instruction, partition)) {
                if (TargetOf(target) is { } followed && !IsInstalled(followed.Entry.Node)) {
                    yield return (followed.Partition, followed.Entry.Node);
                }
            }
        }
    }

    /// <summary>The observed targets of <paramref name="instruction"/> the summary follows: edges
    /// inside the analysed partition are internal, except a call to one of its entries, which is a
    /// new activation and is followed as a self-call.</summary>
    private IEnumerable<ICfgNode> FollowedTargets(CfgInstruction instruction,
        CfgCodePartition partition) {
        foreach (ICfgNode target in TransferTargets(instruction)) {
            if (target.ContainingBlock is { } block
                && _partitionByBlock.TryGetValue(block, out CfgCodePartition? owner)
                && ReferenceEquals(owner, partition)
                && (!instruction.IsCall || !partition.Entries.Any(entry => Equals(entry.Node, target)))) {
                continue;
            }
            yield return target;
        }
    }

    private CallEffect EntrySummary(CfgCodePartition partition, ICfgNode entry,
        CfgCodePartitionEntry? entryMeta) {
        if (entryMeta is not null && _installed.Contains(entryMeta.Address)) {
            return InstalledHandlerSummary(partition);
        }
        return PartitionSummary(partition, entry);
    }

    /// <summary>An emulator-installed handler preserves every register; only the targets of its
    /// <c>int</c> instructions that are program partitions contribute. The user subroutine the
    /// int 74h stub far-calls through the mouse driver is not followed: the effect accounts for
    /// it through <see cref="HardwareInterruptSetup.MouseUserSubroutineRegistered"/>.</summary>
    private CallEffect InstalledHandlerSummary(CfgCodePartition partition) {
        CpuValues reads = CpuValues.None;
        CpuValues writes = CpuValues.None;
        foreach (CfgInstruction instruction in partition.Blocks
                     .SelectMany(block => block.Instructions)
                     .OfType<CfgInstruction>()) {
            if (!IsInterruptCall(instruction)) {
                continue;
            }
            foreach (ICfgNode target in TransferTargets(instruction)) {
                if (TargetOf(target) is not { } followed || _installed.Contains(followed.Entry.Address)) {
                    continue;
                }
                CallEffect summary = SummaryOf(followed.Partition, followed.Entry.Node);
                reads |= summary.Reads;
                writes |= summary.Writes;
            }
        }
        return new CallEffect(reads, writes);
    }

    private CallEffect PartitionSummary(CfgCodePartition partition, ICfgNode entry) {
        if (!_methodByPartition.TryGetValue(partition, out MethodPlan? methodPlan)) {
            return Conservative;
        }
        (IrMethod method, IrSsa ssa, IrStackSlots slots) = LiftedMethodOf(methodPlan);
        IrEntry irEntry = method.Entries.FirstOrDefault(e => Equals(e.Node, entry)) ?? method.Entries[0];
        PartitionAnalysis analysis = new PartitionAnalysis(this, method, ssa, slots, irEntry, partition);

        CpuValues reads = CpuValues.None;
        CpuValues writes = CpuValues.None;
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                if (statement.Node is CheckExternalEventsNode) {
                    continue;
                }
                if (analysis.IsTransferOrigin(statement.Origin)) {
                    if (!analysis.Follow(statement, out CallEffect followed)) {
                        return Conservative;
                    }
                    reads |= followed.Reads;
                    writes |= followed.Writes;
                    continue;
                }
                if (statement.Effects == IrEffects.Unknown) {
                    return Conservative;
                }
                reads |= analysis.EntryReads(statement.Effects.Reads, statement);
            }
            if (!analysis.Terminate(block.Terminator, ref reads, ref writes)) {
                return Conservative;
            }
        }
        return new CallEffect(reads, writes);
    }

    /// <summary>The lifted method, its SSA and its stack slots of a plan: computed once and shared by
    /// every entry summary of the same partition.</summary>
    private (IrMethod Method, IrSsa Ssa, IrStackSlots Slots) LiftedMethodOf(MethodPlan methodPlan) {
        if (_liftedByMethod.TryGetValue(methodPlan,
                out (IrMethod Method, IrSsa Ssa, IrStackSlots Slots) cached)) {
            return cached;
        }
        IrMethod method = _lifter.Lift(methodPlan);
        IrPassPipeline.Run(method, IrPass.InstructionIdioms, verify: false, buildWebs: false,
            new GenerationStepTimer());
        IrSsa ssa = IrSsa.Build(method);
        IrStackSlots slots = IrStackSlots.Compute(method, new IrMemoryAliasAnalysis(method, ssa, _context.Policy),
            _ => null);
        (IrMethod Method, IrSsa Ssa, IrStackSlots Slots) data = (method, ssa, slots);
        _liftedByMethod[methodPlan] = data;
        return data;
    }

    /// <summary>The frame analysis of the lifted method of <paramref name="methodPlan"/>:
    /// computed once per method, over the same neutrally lifted IR the summaries use.</summary>
    private IrFrames FramesOf(MethodPlan methodPlan) {
        if (_framesByMethod.TryGetValue(methodPlan, out IrFrames? frames)) {
            return frames;
        }
        (IrMethod method, IrSsa ssa, IrStackSlots slots) = LiftedMethodOf(methodPlan);
        frames = IrFrames.Compute(method, ssa, slots, statement =>
            statement.Origin is CfgInstruction instruction
                ? CallTargetWrites(instruction)
                : CpuValues.Registers);
        _framesByMethod[methodPlan] = frames;
        return frames;
    }

    /// <summary>Whether the handler entered at <paramref name="entry"/> may observe the FLAGS
    /// image the delivery pushed: an installed stub never touches the frame itself, only the
    /// program handlers its <c>int</c> instructions invoke may; a program handler may when a
    /// stack access reaches the FLAGS word at <see cref="DeliveredFlagsOffset"/> of its delivery
    /// frame or the reach cannot be proven.</summary>
    private bool ObservesDeliveredFlags(CfgCodePartition partition, CfgCodePartitionEntry entry) {
        if (_installed.Contains(entry.Address)) {
            return InstalledTargetsAccessAboveFrame(partition, writes: false);
        }
        return MayAccessFrameRange(partition, entry.Node,
            DeliveredFlagsOffset, DeliveredFlagsOffset + 2, writes: false);
    }

    /// <summary>Whether the handler entered at <paramref name="entry"/> may write the FLAGS image
    /// the delivery pushed: the same reach as <see cref="ObservesDeliveredFlags"/> restricted to
    /// write accesses, so a handler that only reads the image does not dirty the flags.</summary>
    private bool PatchesDeliveredFlags(CfgCodePartition partition, CfgCodePartitionEntry entry) {
        if (_installed.Contains(entry.Address)) {
            return InstalledTargetsAccessAboveFrame(partition, writes: true);
        }
        return MayAccessFrameRange(partition, entry.Node,
            DeliveredFlagsOffset, DeliveredFlagsOffset + 2, writes: true);
    }

    /// <summary>Whether the method entered at <paramref name="entry"/> may perform a stack
    /// access covering a byte in [<paramref name="lowOffset"/>, <paramref name="highOffset"/>)
    /// relative to its own entry SP - a write access only when <paramref name="writes"/> is set:
    /// a proven frame access overlapping the range answers true, an SS access the frame analysis
    /// cannot place may reach it, and a followed call / <c>int</c> target that may access above its
    /// own entry frame reaches the caller's range. A cycle or an unanalysable method or target
    /// answers true: the range cannot be proven untouched.</summary>
    private bool MayAccessFrameRange(CfgCodePartition partition, ICfgNode entry,
        int lowOffset, int highOffset, bool writes) {
        (CfgCodePartition, ICfgNode, int, int, bool) key =
            (partition, entry, lowOffset, highOffset, writes);
        if (_frameRangeAccesses.TryGetValue(key, out bool cached)) {
            return cached;
        }
        if (!_decidingFrameRanges.Add(key)) {
            return true;
        }
        bool result = ComputeFrameRangeAccess(partition, entry, lowOffset, highOffset, writes);
        _decidingFrameRanges.Remove(key);
        _frameRangeAccesses[key] = result;
        return result;
    }

    private bool ComputeFrameRangeAccess(CfgCodePartition partition, ICfgNode entry,
        int lowOffset, int highOffset, bool writes) {
        if (!_methodByPartition.TryGetValue(partition, out MethodPlan? methodPlan)) {
            return true;
        }
        (IrMethod method, IrSsa ssa, IrStackSlots _) = LiftedMethodOf(methodPlan);
        IrFrames frames = FramesOf(methodPlan);
        foreach (IrFrameAccess access in frames.Accesses) {
            if ((!writes || access.IsWrite)
                && access.EntryOffset < highOffset && access.EntryOffset + access.Width > lowOffset) {
                return true;
            }
        }
        IrMemoryAliasAnalysis alias = new(method, ssa);
        Dictionary<SegmentedPointerNode, IrFrameAccess?> byNode =
            new(ReferenceEqualityComparer.Instance);
        foreach ((SegmentedPointerNode pointer, IrFrameAccess? access) in frames.PointerResults) {
            if (!byNode.ContainsKey(pointer) || access is null) {
                byNode[pointer] = access;
            }
        }
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                ValueNode? storeTarget = null;
                if (statement.Node is BinaryOperationNode {
                        BinaryOperation: BinaryOperation.ASSIGN,
                        Left: SegmentedPointerNode or AbsolutePointerNode } assign) {
                    storeTarget = assign.Left;
                }
                foreach (SegmentedPointerNode pointer in IrAstChildren.Descendants(statement.Node)
                             .OfType<SegmentedPointerNode>()) {
                    if (writes && !ReferenceEquals(pointer, storeTarget)) {
                        continue;
                    }
                    if (alias.OfPointer(pointer, statement, block, index).SegmentRegister
                            == IrSsaValue.Ss
                        && (!byNode.TryGetValue(pointer, out IrFrameAccess? access)
                            || access is null)) {
                        return true;
                    }
                }
                if (statement.Origin is CfgInstruction instruction && !instruction.IsReturn
                    && FollowsAboveFrameAccess(instruction, partition, writes)) {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Whether a transfer origin of <paramref name="instruction"/> reaches a target
    /// that may access above its own entry frame: a target inside the analysed partition is an
    /// internal edge whose accesses were already scanned; an unanalysable target answers true.</summary>
    private bool FollowsAboveFrameAccess(CfgInstruction instruction, CfgCodePartition partition,
        bool writes) {
        foreach (ICfgNode target in TransferTargets(instruction)) {
            if (target.ContainingBlock is { } block
                && _partitionByBlock.TryGetValue(block, out CfgCodePartition? owner)
                && ReferenceEquals(owner, partition)) {
                continue;
            }
            if (IsInstalled(target)) {
                if (TargetOf(target) is { } installed
                    && InstalledTargetsAccessAboveFrame(installed.Partition, writes)) {
                    return true;
                }
                continue;
            }
            if (TargetOf(target) is not { } followed
                || TargetAccessesAboveFrame(followed.Partition, followed.Entry.Node, writes)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether the program handlers the installed stub of
    /// <paramref name="installedPartition"/> calls through its <c>int</c> instructions may
    /// access above their own entry frames. Mirrors <see cref="InstalledHandlerSummary"/>: only
    /// analysable program targets are followed.</summary>
    private bool InstalledTargetsAccessAboveFrame(CfgCodePartition installedPartition, bool writes) {
        foreach (CfgInstruction instruction in installedPartition.Blocks
                     .SelectMany(block => block.Instructions)
                     .OfType<CfgInstruction>()) {
            if (!IsInterruptCall(instruction)) {
                continue;
            }
            foreach (ICfgNode target in TransferTargets(instruction)) {
                if (IsInstalled(target)) {
                    continue;
                }
                if (TargetOf(target) is not { } followed
                    || TargetAccessesAboveFrame(followed.Partition, followed.Entry.Node, writes)) {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Whether the method of (<paramref name="partition"/>, <paramref name="entry"/>)
    /// may access at or above the bytes its own entry frame holds (its return address and the
    /// locals below it stay inside its frame; anything at <see cref="IrFrames.ReturnAddressBytes"/>
    /// or beyond reaches the caller). An unanalysable method or frame answers true.</summary>
    private bool TargetAccessesAboveFrame(CfgCodePartition partition, ICfgNode entry, bool writes) {
        if (!_methodByPartition.TryGetValue(partition, out MethodPlan? methodPlan)) {
            return true;
        }
        if (FramesOf(methodPlan).ReturnAddressBytes is not { } returnBytes) {
            return true;
        }
        return MayAccessFrameRange(partition, entry, returnBytes, int.MaxValue, writes);
    }

    /// <summary>Records the observed targets of the <c>int</c> instructions of
    /// <paramref name="partition"/> when it is an installed hardware stub: a chained vector (the
    /// int 1Ch of the int 8 stub) whose IVT target discovery saw delivered is a known handler.</summary>
    private void CollectStubIntTargets(CfgCodePartition partition,
        HashSet<SegmentedAddress> hardwareStubs) {
        if (!partition.Entries.Any(entry => hardwareStubs.Contains(entry.Address))) {
            return;
        }
        foreach (CfgInstruction instruction in partition.Blocks
                .SelectMany(block => block.Instructions)
                .OfType<CfgInstruction>()) {
            if (!IsInterruptCall(instruction)) {
                continue;
            }
            foreach (ICfgNode target in TransferTargets(instruction)) {
                _stubIntTargets.Add(target.Address);
            }
        }
    }

    private bool IsInstalled(ICfgNode node) => _installed.Contains(node.Address);

    internal (CfgCodePartition Partition, CfgCodePartitionEntry Entry)? TargetOf(ICfgNode node) {
        CfgBlock? block = node.ContainingBlock;
        if (block is null || !_partitionByBlock.TryGetValue(block, out CfgCodePartition? partition)) {
            return null;
        }
        CfgCodePartitionEntry? entry = partition.Entries.FirstOrDefault(e => Equals(e.Node, node));
        return entry is null ? null : (partition, entry);
    }

    /// <summary>The observed call / <c>int</c> / jump targets of an instruction: its
    /// <see cref="InstructionSuccessorType.Normal"/> successors minus the plain fall-through.</summary>
    internal static IEnumerable<ICfgNode> TransferTargets(CfgInstruction instruction) {
        if (!instruction.SuccessorsPerType.TryGetValue(InstructionSuccessorType.Normal,
                out ISet<ICfgNode>? successors)) {
            yield break;
        }
        foreach (ICfgNode target in successors
                .Where(target => target.Address
                    != instruction.NextInMemoryAddress32.ToSegmentedAddress())) {
            yield return target;
        }
    }

    internal static bool IsInterruptCall(CfgInstruction instruction) =>
        instruction.ExecutionAst is InterruptCallNode
        || IrAstChildren.Descendants(instruction.ExecutionAst).OfType<InterruptCallNode>().Any();

    /// <summary>The vector an <c>int</c> instruction calls: the constant byte its vector number
    /// resolves to, or <see langword="null"/> when the instruction has no interrupt call or the
    /// number is not a constant byte.</summary>
    internal static byte? InterruptVectorOf(CfgInstruction instruction) {
        List<InterruptCallNode> calls =
            [.. IrAstChildren.Descendants(instruction.ExecutionAst).OfType<InterruptCallNode>()];
        if (calls.Count == 0) {
            return null;
        }
        byte? result = null;
        foreach (InterruptCallNode call in calls) {
            IVisitableAstNode value = call.VectorNumber is InstructionFieldNode field ? field.ResolvedNode : call.VectorNumber;
            if (!ConstantEvaluation.TryEvaluateReplacing(value, _ => null, out ulong vector)
                || vector > byte.MaxValue) {
                return null;
            }
            if (result is { } previous && previous != (byte)vector) {
                return null;
            }
            result = (byte)vector;
        }
        return result;
    }

    /// <summary>Whether every <c>int</c> site of the program on <paramref name="service"/>'s
    /// vector observed only the installed stub, so generated code may call the service
    /// implementation directly at every site: at least one site must target the vector, each
    /// such site must have observed targets and only the stub, and no <c>int</c> anywhere may
    /// have an unresolvable vector (it cannot be proven unrelated). Only recorded instructions
    /// count: a speculative <c>int</c> (<see cref="ICfgNode.IsSpeculative"/>, a failure path
    /// discovery never executed) has no observed target to qualify or disqualify anything and
    /// is skipped. The dump-time vector table
    /// entry, when the snapshot covers it, must still point at the stub: a custom handler that
    /// was seen and later uninstalled is a conflicting live target like any other.</summary>
    internal bool HasOnlyBuiltInTargets(InterruptServiceVector service) {
        EnsureInitialized();
        bool anySite = false;
        foreach (CfgInstruction instruction in _context.Program.Partitions
                .SelectMany(partition => partition.Blocks)
                .SelectMany(block => block.Instructions)
                .OfType<CfgInstruction>().Where(node => !node.IsSpeculative).Distinct()) {
            byte? vector = InterruptVectorOf(instruction);
            if (vector is null) {
                if (IsInterruptCall(instruction)) {
                    return false;
                }
                continue;
            }
            if (vector.Value != service.VectorNumber) {
                continue;
            }
            anySite = true;
            List<SegmentedAddress> targets = TransferTargets(instruction)
                .Select(target => target.Address).ToList();
            // A target the stub's own dispatch callback moved CS:IP to - an EXEC child's entry
            // or a terminate's resume landing - is not a hooked target: the site still invokes
            // only the installed stub.
            if (targets.Count == 0 || targets.Any(target => target != service.StubAddress
                    && !_context.ExecChildEntries.Contains(target))) {
                return false;
            }
        }
        if (!anySite) {
            return false;
        }
        byte[] snapshot = _context.Program.Inputs.MemorySnapshot;
        int vectorEntry = service.VectorNumber * 4;
        if (vectorEntry + 4 > snapshot.Length) {
            return true;
        }
        SegmentedAddress recorded = new SegmentedAddress(
            BinaryPrimitives.ReadUInt16LittleEndian(snapshot.AsSpan(vectorEntry + 2, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(snapshot.AsSpan(vectorEntry, 2)));
        return recorded == service.StubAddress;
    }

    /// <summary>The register summary computation of one lifted partition.</summary>
    private sealed class PartitionAnalysis {
        private readonly HardwareInterruptEffectBuilder _owner;
        private readonly IrSsa _ssa;
        private readonly IrPreservation _preservation;
        private readonly CfgCodePartition _partition;
        private readonly int[] _entryVersions;

        public PartitionAnalysis(HardwareInterruptEffectBuilder owner, IrMethod method, IrSsa ssa,
            IrStackSlots slots, IrEntry entry, CfgCodePartition partition) {
            _owner = owner;
            _ssa = ssa;
            _preservation = new IrPreservation(method, ssa, slots, TransferChanges);
            _partition = partition;
            _entryVersions = SsaValues
                .Select(value => ssa.VersionBefore(entry.Block, 0, value))
                .ToArray();
        }

        /// <summary>The observed call / <c>int</c> / jump targets of <paramref name="instruction"/>
        /// that leave the analysed partition or re-enter it as a call: a target whose block belongs
        /// to it is an internal edge, not a transfer, unless the instruction calls one of its entries.</summary>
        private IEnumerable<ICfgNode> TransferTargets(CfgInstruction instruction) =>
            _owner.FollowedTargets(instruction, _partition);

        /// <summary>Whether the statement's origin instruction transfers out of the partition: a call,
        /// an <c>int</c> or a jump whose observed targets other than the fall-through must all be
        /// analysable program partitions for the statement to be followed instead of opaque.</summary>
        public bool IsTransferOrigin(ICfgNode origin) =>
            origin is CfgInstruction instruction
            && !instruction.IsReturn
            && TransferTargets(instruction).Any();

        /// <summary>Follows the transfer of <paramref name="statement"/>: its own call-helper argument
        /// reads plus the summaries of its observed targets.</summary>
        public bool Follow(IrStatement statement, out CallEffect followed) =>
            FollowTargets((CfgInstruction)statement.Origin, ArgumentReads(statement), out followed);

        /// <summary>Follows the observed targets of <paramref name="origin"/>, every one of which must
        /// be an analysable program partition, adding their summaries to <paramref name="ownReads"/>.
        /// </summary>
        private bool FollowTargets(CfgInstruction origin, CpuValues ownReads,
            out CallEffect followed) {
            CpuValues reads = ownReads;
            CpuValues writes = CpuValues.None;
            foreach (ICfgNode target in TransferTargets(origin)) {
                if (_owner.TargetOf(target) is not { } followedTarget
                    || _owner.IsInstalled(followedTarget.Entry.Node)) {
                    followed = Empty;
                    return false;
                }
                CallEffect summary = _owner.SummaryOf(followedTarget.Partition, followedTarget.Entry.Node);
                reads |= summary.Reads;
                writes |= summary.Writes;
            }
            followed = new CallEffect(reads, writes);
            return true;
        }

        /// <summary>Applies a block terminator to the summary: an <c>iret</c> or <c>ret</c> exit
        /// checkpoints the versions it returns, a partition-exit also follows its targets, an abort
        /// contributes only its own reads, and an internal branch contributes its condition reads.</summary>
        public bool Terminate(IrTerminator terminator, ref CpuValues reads, ref CpuValues writes) {
            if (terminator is not IrExit exit) {
                reads |= EntryReads(terminator.Effects.Reads, terminator);
                return true;
            }
            if (Contains<InterruptReturnNode>(exit.Transfer)
                || exit.Origin is CfgInstruction { IsReturn: true }) {
                writes |= WritesAtExit(terminator);
                return true;
            }
            if (exit.Origin is CfgInstruction origin && TransferTargets(origin).Any()) {
                if (!FollowTargets(origin, CpuValues.None, out CallEffect followed)) {
                    return false;
                }
                reads |= followed.Reads;
                writes |= followed.Writes | WritesAtExit(terminator);
                return true;
            }
            if (Contains<FailAsUntestedNode>(exit.Transfer) || Contains<ThrowNode>(exit.Transfer)) {
                reads |= EntryReads(terminator.Effects.Reads, terminator);
                return true;
            }
            writes |= WritesAtExit(terminator);
            return true;
        }

        private static bool Contains<T>(IVisitableAstNode node) where T : IVisitableAstNode =>
            node is T || IrAstChildren.Descendants(node).OfType<T>().Any();

        /// <summary>The <see cref="CpuValues"/> bits <paramref name="reads"/> covers that can still
        /// hold an entry input at <paramref name="site"/>: the entry version itself, and every
        /// version the analysis cannot prove a full overwrite - a partial definition or a join
        /// (phi) can carry the incoming bytes, so its reads conservatively count as entry reads.
        /// Status flags and DF count the same way: a flag read at its entry version is the FLAGS
        /// image the delivery pushed, which the interrupted code must have computed.</summary>
        public CpuValues EntryReads(CpuValues reads, IrStatement site) =>
            EntryReads(reads, value => _ssa.VersionUsed(site, value));

        private CpuValues EntryReads(CpuValues reads, IrTerminator site) =>
            EntryReads(reads, value => _ssa.VersionUsed(site, value));

        private CpuValues EntryReads(CpuValues reads, Func<IrSsaValue, int> versionUsed) {
            CpuValues result = CpuValues.None;
            for (int i = 0; i < SsaValues.Length; i++) {
                IrSsaValue value = SsaValues[i];
                int version = versionUsed(value);
                if (version < 0 || version == _entryVersions[i]
                    || _ssa.Info(version).Kind != IrSsaDefinitionKind.Definition) {
                    result |= reads & IrSsaValues.MaskOf(value);
                }
            }
            return result;
        }

        /// <summary>The register bits the call / <c>int</c> helper arguments of
        /// <paramref name="statement"/> read at method-entry versions.</summary>
        private CpuValues ArgumentReads(IrStatement statement) {
            CpuValues reads = CpuValues.None;
            foreach (IVisitableAstNode argument in IrAstChildren.Descendants(statement.Node)
                         .SelectMany(ArgumentsOf)) {
                reads |= IrEffectsVisitor.Of(argument, false).Reads;
            }
            return EntryReads(reads, statement);
        }

        private static IEnumerable<IVisitableAstNode> ArgumentsOf(IVisitableAstNode node) => node switch {
            MethodCallNode call => call.Arguments,
            MethodCallValueNode callValue => callValue.CallNode.Arguments,
            _ => []
        };

        /// <summary>The <see cref="CpuValues"/> bits of each register whose version at the terminator
        /// is not preserved.</summary>
        private CpuValues WritesAtExit(IrTerminator terminator) {
            CpuValues result = CpuValues.None;
            for (int i = 0; i < SsaRegisters.Length; i++) {
                int version = _ssa.VersionUsed(terminator, SsaRegisters[i]);
                CpuValues preserved = version >= 0 ? _preservation.Preserved(SsaRegisters[i], version) : CpuValues.None;
                if (version >= 0) {
                    result |= IrSsaValues.MaskOf(SsaRegisters[i])
                        & ~preserved;
                }
            }
            return result;
        }

        /// <summary>The transfer effect of <paramref name="statement"/> for
        /// <see cref="IrPreservation"/>: the writes of the current effect for an event check
        /// (neutral while <see cref="ComputeEffect"/> runs, the computed effect afterwards), the
        /// writes of the followed targets of a call / <c>int</c> that all resolve, every register
        /// for a transfer origin that does not resolve, null for any other statement.</summary>
        private CpuValues? TransferChanges(IrStatement statement) {
            if (statement.Node is CheckExternalEventsNode) {
                return _owner._eventCheckWrites;
            }
            if (IsTransferOrigin(statement.Origin)) {
                return FollowedTargetsResolve(statement, out CpuValues clobbered)
                    ? clobbered
                    : CpuValues.All;
            }
            return null;
        }

        /// <summary>The register bits the followed targets of a call / <c>int</c> statement may
        /// write; <see langword="false"/> when one target is not an analysable program partition.</summary>
        private bool FollowedTargetsResolve(IrStatement statement, out CpuValues clobbered) {
            clobbered = CpuValues.None;
            foreach (ICfgNode target in TransferTargets((CfgInstruction)statement.Origin)) {
                if (_owner.TargetOf(target) is not { } followed
                    || _owner.IsInstalled(followed.Entry.Node)) {
                    return false;
                }
                clobbered |= _owner.SummaryOf(followed.Partition, followed.Entry.Node).Writes;
            }
            return true;
        }
    }
}
