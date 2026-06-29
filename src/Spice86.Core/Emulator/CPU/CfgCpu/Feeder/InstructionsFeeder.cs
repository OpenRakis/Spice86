namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Runtime.CompilerServices;

using SequentialIdAllocator = Spice86.Shared.Utils.SequentialIdAllocator;

/// <summary>
/// Responsible for getting parsed instructions at a given address from memory.
/// Instructions are parsed only once per address, after that they are retrieved from a cache
/// Self modifying code is detected and supported.
/// If an instruction is modified and then put back in its original version, it is the original instance of the parsed instruction that will be returned for the address
/// Instructions can be replaced in the cache (see method ReplaceInstruction)
/// </summary>
public class InstructionsFeeder : IClearable {
    private readonly InstructionParser _instructionParser;
    private readonly SignatureReducer _signatureReducer;
    private readonly CfgNodeExecutionCompiler _executionCompiler;
    private readonly SpeculativeExplorer? _speculativeExplorer;

    public InstructionsFeeder(EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory, State cpuState,
        InstructionReplacerRegistry replacerRegistry, CfgNodeExecutionCompiler executionCompiler, SequentialIdAllocator idAllocator,
        NodeLinker? nodeLinker) {
        _instructionParser = new(memory, cpuState, idAllocator);
        _executionCompiler = executionCompiler;
        CurrentInstructions = new(memory, emulatorBreakpointsManager, replacerRegistry);
        PreviousInstructions = new(memory, replacerRegistry);
        _signatureReducer = new(replacerRegistry);
        NodeIndex = new(replacerRegistry);
        if (nodeLinker is not null) {
            _speculativeExplorer = new(_instructionParser, NodeIndex, nodeLinker);
            SpeculativePromoter = new(executionCompiler, CurrentInstructions, PreviousInstructions);
        }
    }
    
    public CurrentInstructions CurrentInstructions { get; }
    public PreviousInstructions PreviousInstructions { get; }
    public CfgNodeIndex NodeIndex { get; }
    public SpeculativePromoter? SpeculativePromoter { get; }
    public SpeculativeReconciler? SpeculativeReconciler { get; private set; }

    /// <summary>
    /// Seeds known-safe interrupt handler entry points for speculative exploration with
    /// continuation-following enabled. Each address is decoded speculatively (if not already
    /// in the index) and its full handler body is explored including post-call/int continuations.
    /// No-op when the speculative explorer is disabled.
    /// </summary>
    /// <param name="handlerAddresses">Entry addresses of emulator-installed interrupt handlers.</param>
    public void SeedKnownSafeHandlers(IReadOnlyList<SegmentedAddress> handlerAddresses) {
        if (_speculativeExplorer is null) {
            return;
        }
        foreach (SegmentedAddress entry in handlerAddresses) {
            _speculativeExplorer.SeedKnownSafe(entry);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CfgInstruction GetInstructionFromMemory(SegmentedAddress address) {
        // Try to get instruction from cache that represents current memory state.
        CfgInstruction? current = CurrentInstructions.GetAtAddress(address);
        if (current != null) {
            return current;
        }

        // Instruction was not in cache.
        // Either it has never been parsed or it has been evicted from cache due to self modifying code.
        return GetFromPreviousOrParse(address);
    }

    private CfgInstruction GetFromPreviousOrParse(SegmentedAddress address) {
        // First try to see if it has been encountered before at this address instead of re-parsing.
        // Reason is we don't want several versions of the same instructions hanging around in the graph,
        // this would be bad for successors / predecessors management and self modifying code detection.
        // At this stage, we don't care about non-final fields.
        // Even if instruction existed before with different non-final field, we create a new instruction.
        // Reason is we don't want to deal with reducing fields here.
        CfgInstruction? previousMatching = PreviousInstructions.GetAtAddressIfMatchesMemory(address);
        if (previousMatching != null) {
            CurrentInstructions.SetAsCurrent(previousMatching);
            return previousMatching;
        }

        return ParseCheckingExistingSpeculative(address);
    }

    internal void SetSpeculativePruner(SpeculativeReachabilityPruner pruner) {
        if (SpeculativePromoter is not null) {
            SpeculativeReconciler = new(SpeculativePromoter, pruner, NodeIndex);
        }
    }

    private CfgInstruction ParseCheckingExistingSpeculative(SegmentedAddress address) {
        if (SpeculativeReconciler is null) {
            return ParseAndSetAsCurrent(address);
        }
        CfgInstruction? existingSpeculative = NodeIndex.GetAtAddress(address)
            .FirstOrDefault(n => n.IsSpeculative);
        if (existingSpeculative is null) {
            return ParseAndSetAsCurrent(address);
        }
        // Light parse only for signature comparison — avoids SignatureReducer fan-out
        // and discards the node cheaply on the fast path (promotion).
        CfgInstruction parsed = _instructionParser.ParseInstructionAt(address);
        if (SpeculativeReconciler.Reconcile(existingSpeculative, parsed.Signature, address)) {
            return existingSpeculative;
        }
        // Signatures differ: run the full uniqueness check (signature reduction against
        // previous instructions) so we reuse an existing node when possible.
        CfgInstruction live = ReduceAgainstPrevious(parsed, address);
        CommitObserved(live);
        return live;
    }
    internal CfgInstruction ParseAndSetAsCurrent(SegmentedAddress address) {
        CfgInstruction parsed = ParseEnsuringUnique(address);
        CommitObserved(parsed);
        _speculativeExplorer?.ExploreFrom(parsed);
        return parsed;
    }

    private void CommitObserved(CfgInstruction instruction) {
        _executionCompiler.Compile(instruction);
        CurrentInstructions.SetAsCurrent(instruction);
        PreviousInstructions.AddInstructionInPrevious(instruction);
        NodeIndex.Insert(instruction);
    }

    internal CfgInstruction ParseEnsuringUnique(SegmentedAddress address) {
        CfgInstruction parsed = _instructionParser.ParseInstructionAt(address);
        return ReduceAgainstPrevious(parsed, address);
    }

    private CfgInstruction ReduceAgainstPrevious(CfgInstruction parsed, SegmentedAddress address) {
        HashSet<CfgInstruction>? previousSet = PreviousInstructions.GetAtAddress(address);
        if (previousSet != null) {
            foreach (CfgInstruction existing in previousSet) {
                CfgInstruction? reduced = _signatureReducer.ReduceToOne(parsed, existing);
                if (reduced != null) {
                    return reduced;
                }
            }
        }
        return parsed;
    }

    /// <summary>
    /// Clears all internal state in both current and previous instruction caches.
    /// </summary>
    public void Clear() {
        CurrentInstructions.Clear();
        PreviousInstructions.Clear();
    }
}