namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The shared lookup table all emitters read from: "which partition owns this node?", "what segment variable
/// name?", "where does this edge go?". Built once during analysis, then treated as a frozen dictionary for the
/// rest of the pipeline.
/// </summary>
internal sealed class CfgGeneratorContext {
    private readonly Dictionary<ICfgNode, CfgCodePartition> _partitionByNode;
    private readonly Dictionary<CfgCodePartition, string> _methodNames;
    private readonly Dictionary<CfgCodePartition, string> _partitionBaseNames;
    private readonly Dictionary<ResolvedCfgEdge, CfgCodePartitionTransfer> _transfersByEdge;
    private readonly Dictionary<(ICfgNode Source, ICfgNode Target, InstructionSuccessorType SuccessorType), List<CfgCodePartitionTransfer>> _transfersByNodes;
    private readonly Dictionary<CfgCodePartition, IReadOnlyList<CfgCodePartitionEntry>> _entriesByPartition;
    private readonly Dictionary<CfgInstruction, string> _signatureFieldByInstruction;

    public CfgGeneratorContext(
        CfgPartitionedProgram program,
        Dictionary<ICfgNode, CfgCodePartition> partitionByNode,
        Dictionary<CfgCodePartition, string> methodNames,
        Dictionary<CfgCodePartition, string> partitionBaseNames,
        Dictionary<ushort, string> segmentVariables,
        Dictionary<ResolvedCfgEdge, CfgCodePartitionTransfer> transfersByEdge,
        Dictionary<CfgCodePartition, IReadOnlyList<CfgCodePartitionEntry>> entriesByPartition,
        IReadOnlyList<SignatureFieldPlan> signatureFields) {
        Program = program;
        _partitionByNode = partitionByNode;
        _methodNames = methodNames;
        _partitionBaseNames = partitionBaseNames;
        SegmentVariables = segmentVariables;
        _transfersByEdge = transfersByEdge;
        _transfersByNodes = [];
        foreach (KeyValuePair<ResolvedCfgEdge, CfgCodePartitionTransfer> entry in transfersByEdge) {
            (ICfgNode Source, ICfgNode Target, InstructionSuccessorType SuccessorType) key =
                (entry.Key.Source, entry.Key.Target, entry.Key.SuccessorType);
            if (!_transfersByNodes.TryGetValue(key, out List<CfgCodePartitionTransfer>? list)) {
                list = [];
                _transfersByNodes[key] = list;
            }
            list.Add(entry.Value);
        }
        _entriesByPartition = entriesByPartition;
        SignatureFields = signatureFields;
        _signatureFieldByInstruction = signatureFields.ToDictionary(
            field => field.Instruction,
            field => field.FieldName);
    }

    public CfgPartitionedProgram Program { get; }

    /// <summary>Every signature field the generated class declares, in emission order.</summary>
    public IReadOnlyList<SignatureFieldPlan> SignatureFields { get; }

    /// <summary>
    /// The name of the generated field holding <paramref name="instruction"/>'s signature bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No field was planned for the instruction. The collection and the emission rules disagree, which is a
    /// generator bug rather than a property of the program under test.
    /// </exception>
    public string GetSignatureField(CfgInstruction instruction) {
        if (!_signatureFieldByInstruction.TryGetValue(instruction, out string? fieldName)) {
            throw new InvalidOperationException(
                $"No signature field was planned for instruction {instruction.Address} (id {instruction.Id}).");
        }
        return fieldName;
    }

    public CfgCodePartition GetPartition(ICfgNode node) => _partitionByNode[node];
    public string GetMethodName(CfgCodePartition partition) => _methodNames[partition];

    /// <summary>
    /// The address-free base name of a partition (e.g. <c>unknown</c> or a catalogued function label), before
    /// the C# generator appends the single owning address suffix. Registration names for secondary entries are
    /// built from this rather than from <see cref="GetMethodName"/> so the dumped symbol carries exactly one
    /// address triplet and round-trips through the Ghidra symbol file without accumulating duplicates.
    /// </summary>
    public string GetPartitionBaseName(CfgCodePartition partition) => _partitionBaseNames[partition];
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
        if (!_transfersByNodes.TryGetValue((source, target, successorType), out List<CfgCodePartitionTransfer>? list)) {
            return null;
        }
        List<CfgCodePartitionTransfer> matches = list.Distinct().ToList();
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

        IReadOnlyList<ResolvedCfgEdge> redirected = GetSuccessorEdges(call, InstructionSuccessorType.CallToRedirectedReturn)
            .OrderBy(edge => edge.Target.Address.Segment)
            .ThenBy(edge => edge.Target.Address.Offset)
            .ToList();

        IReadOnlyList<ResolvedCfgEdge> aligned = GetSuccessorEdges(call, InstructionSuccessorType.CallToReturn);
        if (aligned.Count > 1) {
            throw new NotSupportedException(
                $"Call {call.Address} has {aligned.Count} observed aligned continuations; the generator cannot pick one continuation for a single call.");
        }
        if (aligned.Count == 1) {
            return new CallContinuation(expectedReturn, aligned[0], redirected);
        }

        IReadOnlyList<ResolvedCfgEdge> misaligned = GetSuccessorEdges(call, InstructionSuccessorType.CallToMisalignedReturn);
        if (misaligned.Count == 1) {
            return new CallContinuation(expectedReturn, misaligned[0], redirected);
        }

        // Several observed misaligned continuations means a shared trampoline-style call whose callee
        // returns to a different address per calling context (e.g. an overlay/interrupt thunk). The
        // generator cannot bake a single static post-call edge, but it does not need to: the call helper
        // ExecuteCallEnsuringSameStack resolves the actual return target at runtime and dispatches into
        // whichever continuation partition the stack unwinds to. So no static continuation is recorded here.
        return new CallContinuation(expectedReturn, ObservedContinuationEdge: null, redirected);
    }

    /// <summary>
    /// The partition entries discovery observed the call's callee transferring control to while the call's
    /// frame was still open: the targets of the callee partition's dynamic returns and same-activation
    /// flows that land on an entry, over every partition reachable through transfers that keep the frame
    /// open. A landing's own partition runs inside the same open frame, so its transfers are collected
    /// too. Calls and CPU faults cross an activation boundary - the nested call dispatches its own
    /// landings and a fault runs in its own execution context - so they are not traversed. The expected
    /// return address is not a landing: the call's dispatch recognizes it before consulting this list.
    /// </summary>
    /// <param name="calleePartition">The partition the call site transfers to.</param>
    /// <param name="expectedReturn">The address the call expects to return to.</param>
    public IReadOnlyList<(CfgCodePartition Partition, CfgCodePartitionEntry Entry)> ResolveCallLandings(
        CfgCodePartition calleePartition, SegmentedAddress expectedReturn) =>
        ResolveLandings(calleePartition, expectedReturn).Entries;

    /// <summary>The landings of a CPU fault handler's activation: every frame-open transfer target
    /// reachable from <paramref name="handlerPartition"/> - the addresses discovery observed the
    /// fault resuming at, whether or not a partition entry sits there.</summary>
    /// <param name="handlerPartition">The partition the fault edge transfers to.</param>
    internal LandingResolution ResolveFaultResumeTargets(CfgCodePartition handlerPartition) =>
        ResolveLandings(handlerPartition, null);

    /// <summary>The frame-open landings of an activation started at <paramref name="partition"/>,
    /// split into targets that are partition entries and those that are not.</summary>
    /// <param name="partition">The partition the activation transfers to.</param>
    /// <param name="excludeTarget">An address that is not a landing (the call's expected return, which
    /// its dispatch recognizes before consulting the list), or <see langword="null"/> to list every target.</param>
    private LandingResolution ResolveLandings(CfgCodePartition partition, SegmentedAddress? excludeTarget) {
        List<(CfgCodePartition, CfgCodePartitionEntry)> entries = [];
        List<SegmentedAddress> unresolved = [];
        HashSet<SegmentedAddress> armed = [];
        HashSet<CfgCodePartition> visited = [partition];
        Queue<CfgCodePartition> pending = new();
        pending.Enqueue(partition);
        while (pending.Count > 0) {
            CfgCodePartition current = pending.Dequeue();
            foreach (CfgCodePartitionTransfer transfer in Program.Transfers) {
                if (!current.Equals(transfer.FromPartition) || !KeepsCallFrameOpen(transfer.Kind)) {
                    continue;
                }
                if (visited.Add(transfer.ToPartition)) {
                    pending.Enqueue(transfer.ToPartition);
                }
                if (transfer.Target == excludeTarget || !armed.Add(transfer.Target)) {
                    continue;
                }
                CfgCodePartitionEntry? entry = GetEntries(transfer.ToPartition)
                    .Where(candidate => candidate.Address == transfer.Target)
                    .Cast<CfgCodePartitionEntry?>()
                    .FirstOrDefault();
                if (entry is { } found) {
                    entries.Add((transfer.ToPartition, found));
                } else {
                    unresolved.Add(transfer.Target);
                }
            }
        }
        return new LandingResolution(
            entries.OrderBy(landing => landing.Item2.Address.Linear).ToList(),
            unresolved.OrderBy(target => target.Linear).ToList());
    }

    /// <summary>The frame-open landings of one activation: the transfer targets that are partition
    /// entries (each with its partition and entry) and the ones that are not.</summary>
    /// <param name="Entries">Landing addresses that are entries of their partition, sorted by address.</param>
    /// <param name="UnresolvedTargets">Observed landing addresses no partition entry covers, sorted by address.</param>
    internal sealed record LandingResolution(
        IReadOnlyList<(CfgCodePartition Partition, CfgCodePartitionEntry Entry)> Entries,
        IReadOnlyList<SegmentedAddress> UnresolvedTargets);

    private static bool KeepsCallFrameOpen(CfgCodePartitionTransferKind kind) =>
        kind is CfgCodePartitionTransferKind.DynamicReturn
            or CfgCodePartitionTransferKind.CrossPartitionFlow
            or CfgCodePartitionTransferKind.CyclicCrossPartitionFlow;
}
