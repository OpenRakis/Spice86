namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The shared lookup table all emitters read from: "which partition owns this node?", "what label does it
/// have?", "what segment variable name?", "where does this edge go?". Built once during analysis, then
/// treated as a frozen dictionary for the rest of the pipeline.
/// </summary>
internal sealed class CfgGeneratorContext {
    private readonly Dictionary<ICfgNode, CfgCodePartition> _partitionByNode;
    private readonly Dictionary<CfgCodePartition, string> _methodNames;
    private readonly Dictionary<CfgCodePartition, string> _partitionBaseNames;
    private readonly Dictionary<ICfgNode, string> _labels;
    private readonly Dictionary<ResolvedCfgEdge, CfgCodePartitionTransfer> _transfersByEdge;
    private readonly Dictionary<CfgCodePartition, IReadOnlyList<CfgCodePartitionEntry>> _entriesByPartition;
    private readonly Dictionary<SegmentedAddress, ICfgNode> _blockEntryByAddress;

    public CfgGeneratorContext(
        CfgPartitionedProgram program,
        Dictionary<ICfgNode, CfgCodePartition> partitionByNode,
        Dictionary<CfgCodePartition, string> methodNames,
        Dictionary<CfgCodePartition, string> partitionBaseNames,
        Dictionary<ICfgNode, string> labels,
        Dictionary<ushort, string> segmentVariables,
        Dictionary<ResolvedCfgEdge, CfgCodePartitionTransfer> transfersByEdge,
        Dictionary<CfgCodePartition, IReadOnlyList<CfgCodePartitionEntry>> entriesByPartition,
        Dictionary<SegmentedAddress, ICfgNode> blockEntryByAddress) {
        Program = program;
        _partitionByNode = partitionByNode;
        _methodNames = methodNames;
        _partitionBaseNames = partitionBaseNames;
        _labels = labels;
        SegmentVariables = segmentVariables;
        _transfersByEdge = transfersByEdge;
        _entriesByPartition = entriesByPartition;
        _blockEntryByAddress = blockEntryByAddress;
    }

    public CfgPartitionedProgram Program { get; }

    public CfgCodePartition GetPartition(ICfgNode node) => _partitionByNode[node];
    public string GetMethodName(CfgCodePartition partition) => _methodNames[partition];

    /// <summary>
    /// The address-free base name of a partition (e.g. <c>unknown</c> or a catalogued function label), before
    /// the C# generator appends the single owning address suffix. Registration names for secondary entries are
    /// built from this rather than from <see cref="GetMethodName"/> so the dumped symbol carries exactly one
    /// address triplet and round-trips through the Ghidra symbol file without accumulating duplicates.
    /// </summary>
    public string GetPartitionBaseName(CfgCodePartition partition) => _partitionBaseNames[partition];
    public string GetLabel(ICfgNode node) => _labels[node];
    public string GetSegmentVariable(ushort segment) => SegmentVariables[segment];
    public IReadOnlyDictionary<ushort, string> SegmentVariables { get; }
    public IReadOnlyList<CfgCodePartitionEntry> GetEntries(CfgCodePartition partition) => _entriesByPartition[partition];

    public int GetEntryLoadOffset(CfgCodePartition partition, ICfgNode entryNode) {
        CfgCodePartitionEntry primary = GetEntries(partition)[0];
        return entryNode.Equals(primary.Node) ? 0 : entryNode.Address.Offset;
    }

    public CfgCodePartitionTransfer? FindTransfer(ResolvedCfgEdge edge) =>
        _transfersByEdge.TryGetValue(edge, out CfgCodePartitionTransfer? transfer) ? transfer : null;

    public CfgCodePartitionTransfer? FindTransfer(ICfgNode source, ICfgNode target, InstructionSuccessorType successorType) {
        List<CfgCodePartitionTransfer> matches = _transfersByEdge
            .Where(entry => ReferenceEquals(entry.Key.Source, source)
                && ReferenceEquals(entry.Key.Target, target)
                && entry.Key.SuccessorType == successorType)
            .Select(entry => entry.Value)
            .Distinct()
            .ToList();
        return matches.Count switch {
            0 => null,
            1 => matches[0],
            _ => throw new NotSupportedException($"Edge {source.Address} -> {target.Address} has {matches.Count} partition transfer identities for successor type {successorType}.")
        };
    }

    public IReadOnlyList<ResolvedCfgEdge> GetSuccessorEdges(CfgInstruction source, InstructionSuccessorType type) {
        if (!source.SuccessorsPerType.TryGetValue(type, out ISet<ICfgNode>? successors)) {
            return [];
        }

        return successors
            .OrderBy(successor => successor.Address.Linear)
            .Select(target => {
                CfgCodePartitionTransfer? transfer = FindTransfer(source, target, type);
                return new ResolvedCfgEdge(source, target, type, transfer?.Kind);
            })
            .ToList();
    }

    public ResolvedCfgEdge ResolveEdge(CfgInstruction source, InstructionSuccessorType type, SegmentedAddress targetAddress) {
        return TryResolveEdge(source, type, targetAddress)
            ?? throw new NotSupportedException($"Instruction {source.Address} has no observed {type} successor to {targetAddress}.");
    }

    /// <summary>
    /// Resolves the single observed successor of <paramref name="source"/> of the given <paramref name="type"/>
    /// whose target is <paramref name="targetAddress"/>. Returns <c>null</c> when none was observed, and throws
    /// when more than one matches (ambiguous semantic dispatch).
    /// </summary>
    public ResolvedCfgEdge? TryResolveEdge(CfgInstruction source, InstructionSuccessorType type, SegmentedAddress targetAddress) {
        List<ResolvedCfgEdge> matches = GetSuccessorEdges(source, type)
            .Where(edge => edge.Target.Address == targetAddress)
            .ToList();
        return matches.Count switch {
            0 => null,
            1 => matches[0],
            _ => throw new NotSupportedException($"Instruction {source.Address} has {matches.Count} observed {type} successors to {targetAddress}; semantic edge dispatch is ambiguous.")
        };
    }

    /// <summary>
    /// Resolves a statically-known constant jump/fallthrough target that was never observed as a runtime edge
    /// but whose target instruction was nonetheless discovered as a block-entry node living in the same
    /// partition as <paramref name="source"/>. The edge is synthesized so the generator can emit a normal
    /// same-method <c>goto</c> instead of failing as untested: the target node exists, only this particular
    /// edge was never traversed during discovery.
    /// <para>
    /// Returns <c>null</c> when no block-entry node exists at <paramref name="targetAddress"/> (the target was
    /// never discovered) or when it lives in a different partition (a cross-partition transfer would need
    /// transfer metadata that was never recorded). Both cases remain genuinely untested.
    /// </para>
    /// </summary>
    public ResolvedCfgEdge? TryResolveSamePartitionBlockEntry(CfgInstruction source, SegmentedAddress targetAddress) {
        if (!_blockEntryByAddress.TryGetValue(targetAddress, out ICfgNode? target)) {
            return null;
        }
        if (!ReferenceEquals(GetPartition(source), GetPartition(target))) {
            return null;
        }
        return new ResolvedCfgEdge(source, target, InstructionSuccessorType.Normal, null);
    }

    /// <summary>
    /// Resolves the post-call continuation for a call-like instruction.
    /// <para>
    /// The expected return address is the instruction that statically follows the call in memory
    /// (<see cref="CfgInstruction.NextInMemoryAddress32"/>). This is the value the CPU pushes and the value
    /// the generated call helpers validate against, and it is always available even when the callee never
    /// returned during discovery.
    /// </para>
    /// <para>
    /// The observed continuation edge is matched by typed successor: an aligned <see cref="InstructionSuccessorType.CallToReturn"/>
    /// edge when present, otherwise a single <see cref="InstructionSuccessorType.CallToMisalignedReturn"/> edge. When more than
    /// one misaligned continuation was observed the call is a shared trampoline-style thunk whose callee returns to a different
    /// address per calling context; no single static continuation can be picked, so none is recorded and the runtime call helper
    /// (<c>ExecuteCallEnsuringSameStack</c>) dispatches to the actual return target instead. When no continuation edge was observed
    /// the callee did not return during discovery, which is a supported behaviour: the helper still runs, but the generator must
    /// fail as untested if the post-call path is reached at runtime.
    /// </para>
    /// </summary>
    public CallContinuation ResolveCallContinuation(CfgInstruction call) {
        SegmentedAddress expectedReturn = call.NextInMemoryAddress32.ToSegmentedAddress();

        IReadOnlyList<ResolvedCfgEdge> aligned = GetSuccessorEdges(call, InstructionSuccessorType.CallToReturn);
        if (aligned.Count > 1) {
            throw new NotSupportedException(
                $"Call {call.Address} has {aligned.Count} observed aligned continuations; the generator cannot pick one continuation for a single call.");
        }
        if (aligned.Count == 1) {
            return new CallContinuation(expectedReturn, aligned[0]);
        }

        IReadOnlyList<ResolvedCfgEdge> misaligned = GetSuccessorEdges(call, InstructionSuccessorType.CallToMisalignedReturn);
        if (misaligned.Count == 1) {
            return new CallContinuation(expectedReturn, misaligned[0]);
        }

        // Several observed misaligned continuations means a shared trampoline-style call whose callee
        // returns to a different address per calling context (e.g. an overlay/interrupt thunk). The
        // generator cannot bake a single static post-call edge, but it does not need to: the call helper
        // ExecuteCallEnsuringSameStack resolves the actual return target at runtime and dispatches into
        // whichever continuation partition the stack unwinds to. So no static continuation is recorded here.
        return new CallContinuation(expectedReturn, ObservedContinuationEdge: null);
    }
}
