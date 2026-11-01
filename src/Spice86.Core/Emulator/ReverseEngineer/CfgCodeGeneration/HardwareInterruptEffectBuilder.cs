namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

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
/// cannot follow - an unknown effect, an unknown call or jump target, a cycle, a hardware vector
/// whose IVT entry is not a known handler - yields the conservative set. A call or jump target
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
    /// DF: the iret restores FLAGS.</summary>
    public const CpuValues InterruptHandlerRegisters = CpuValues.Eax | CpuValues.Ecx
        | CpuValues.Edx | CpuValues.Ebx | CpuValues.Ebp | CpuValues.Esi
        | CpuValues.Edi | CpuValues.Es | CpuValues.Cs | CpuValues.Ds
        | CpuValues.Fs | CpuValues.Gs;

    /// <summary>The effect to assume when the program's hardware interrupt handlers are unknown:
    /// every register a handler can observe may be read and may be changed.</summary>
    internal static CallEffect Conservative { get; } =
        new(InterruptHandlerRegisters, InterruptHandlerRegisters);

    private static CallEffect Empty { get; } = new(CpuValues.None, CpuValues.None);

    private static readonly IrSsaValue[] SsaRegisters = IrValueSets.ValuesIn(CpuValues.Registers);

    private readonly CfgGeneratorContext _context;
    private readonly TransferEmitter _transferEmitter;
    private readonly AstInstructionRenderer _assemblyRenderer;
    private IrLifter _lifter;
    private readonly GenerationPlan _plan;
    private readonly Dictionary<CfgBlock, CfgCodePartition> _partitionByBlock = new();
    private readonly Dictionary<CfgCodePartition, MethodPlan> _methodByPartition = new();
    private readonly Dictionary<(CfgCodePartition Partition, ICfgNode Entry), CallEffect> _summaries = new();
    private readonly Dictionary<MethodPlan, (IrMethod Method, IrSsa Ssa, IrStackSlots Slots)>
        _liftedByMethod = new();
    private readonly HashSet<(CfgCodePartition Partition, ICfgNode Entry)> _inProgress = new();
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
        }
        if (setup.MouseUserSubroutineRegistered) {
            reads |= MouseUserSubroutineUses;
            writes |= MouseUserSubroutineWrites;
        }
        return new CallEffect(
            reads & InterruptHandlerRegisters,
            writes & InterruptHandlerRegisters);
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
        if (!_inProgress.Add(key)) {
            return Conservative;
        }
        CfgCodePartitionEntry? entryMeta = partition.Entries.FirstOrDefault(e => Equals(e.Node, entry));
        CallEffect result = EntrySummary(partition, entry, entryMeta);
        _inProgress.Remove(key);
        _summaries[key] = result;
        return result;
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
        IrStackSlots slots = IrStackSlots.Compute(method, new IrMemoryAliasAnalysis(ssa));
        (IrMethod Method, IrSsa Ssa, IrStackSlots Slots) data = (method, ssa, slots);
        _liftedByMethod[methodPlan] = data;
        return data;
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
            _entryVersions = SsaRegisters
                .Select(value => ssa.VersionBefore(entry.Block, 0, value))
                .ToArray();
        }

        /// <summary>The observed call / <c>int</c> / jump targets of <paramref name="instruction"/>
        /// that leave the analysed partition: a target whose block belongs to it is an internal
        /// edge, not a transfer.</summary>
        private IEnumerable<ICfgNode> TransferTargets(CfgInstruction instruction) {
            foreach (ICfgNode target in HardwareInterruptEffectBuilder.TransferTargets(instruction)) {
                if (target.ContainingBlock is { } block
                    && _owner._partitionByBlock.TryGetValue(block, out CfgCodePartition? partition)
                    && partition == _partition) {
                    continue;
                }
                yield return target;
            }
        }

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
        /// (phi) can carry the incoming bytes, so its reads conservatively count as entry reads.</summary>
        public CpuValues EntryReads(CpuValues reads, IrStatement site) =>
            EntryReads(reads, value => _ssa.VersionUsed(site, value));

        private CpuValues EntryReads(CpuValues reads, IrTerminator site) =>
            EntryReads(reads, value => _ssa.VersionUsed(site, value));

        private CpuValues EntryReads(CpuValues reads, Func<IrSsaValue, int> versionUsed) {
            CpuValues result = CpuValues.None;
            for (int i = 0; i < SsaRegisters.Length; i++) {
                IrSsaValue value = SsaRegisters[i];
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
