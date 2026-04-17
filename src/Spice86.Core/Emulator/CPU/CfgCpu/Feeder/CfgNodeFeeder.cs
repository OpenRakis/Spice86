namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Exceptions;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;

/// <summary>
/// Handles coherency between the memory and the graph of instructions executed by the CPU.
/// Next node to execute is normally the next node from the graph but several checks are done to make sure it is really it:
///  - The node is not null (otherwise it is taken from memory)
///  - If the node is an assembly node, it is the same as what is currently in memory, otherwise it means self modifying code is being detected
///  - If self modifying code is being detected, Selector node is being injected instead.
/// Once the node to execute is determined, it is linked to the previously executed node in the execution context if possible.
/// </summary>
public class CfgNodeFeeder {
    private readonly State _state;
    private readonly NodeLinker _nodeLinker;

    public CfgNodeFeeder(IMemory memory, State state, EmulatorBreakpointsManager emulatorBreakpointsManager,
        InstructionReplacerRegistry replacerRegistry, CfgNodeExecutionCompiler executionCompiler) {
        _state = state;
        InstructionsFeeder = new(emulatorBreakpointsManager, memory, state, replacerRegistry, executionCompiler);
        _nodeLinker = new(replacerRegistry, executionCompiler);
    }

    public InstructionsFeeder InstructionsFeeder { get; }

    /// <summary>
    /// Parses or retrieves from cache the instruction at the current IP from memory.
    /// May trigger SignatureReducer and InstructionReplacerRegistry side effects.
    /// </summary>
    public CfgInstruction GetInstructionFromMemoryAtIp() =>
        InstructionsFeeder.GetInstructionFromMemory(_state.IpSegmentedAddress);

    public ICfgNode GetLinkedCfgNodeToExecute(ExecutionContext executionContext) {
        ICfgNode toExecute = DetermineToExecute(executionContext);
        ICfgNode? lastExecuted = executionContext.LastExecuted;
        if (lastExecuted is not { CanHaveMoreSuccessors: true }) {
            return toExecute;
        }

        // Node can still have successors, try to register the link in the graph
        InstructionSuccessorType type = executionContext.CpuFault
            ? InstructionSuccessorType.CpuFault
            : InstructionSuccessorType.Normal;
        // Reset it
        executionContext.CpuFault = false;

        return _nodeLinker.Link(type, lastExecuted, toExecute);
    }

    private ICfgNode DetermineToExecute(ExecutionContext executionContext) {
        ICfgNode? currentFromGraph = executionContext.NodeToExecuteNextAccordingToGraph;
        if (currentFromGraph == null) {
            return GetInstructionFromMemoryAtIp();
        }
        if (currentFromGraph.IsLive) {
            // Instruction is up to date. No need to do anything.
            return currentFromGraph;
        }
        return ReconcileGraphWithMemory(executionContext, currentFromGraph);
    }

    /// <summary>
    /// The graph node is stale (memory changed since it was parsed).
    /// Parsing the current memory instruction may trigger SignatureReducer if it is reducible with
    /// the stale graph node (same opcode, different non-final fields). The reduction propagates via
    /// InstructionReplacerRegistry -> ExecutionContextManager, updating
    /// executionContext.NodeToExecuteNextAccordingToGraph from staleGraphNode to the merged instruction.
    /// If after that the graph still disagrees with memory, a SelectorNode is injected.
    /// </summary>
    private ICfgNode ReconcileGraphWithMemory(ExecutionContext executionContext, ICfgNode staleGraphNode) {
        CfgInstruction fromMemory = GetInstructionFromMemoryAtIp();
        ICfgNode? graphNodeAfterReconciliation = executionContext.NodeToExecuteNextAccordingToGraph;

        // If the replacer merged the stale node into fromMemory, the graph reference
        // now points to fromMemory. Reconciliation succeeded.
        if (ReferenceEquals(fromMemory, graphNodeAfterReconciliation)) {
            return fromMemory;
        }

        if (graphNodeAfterReconciliation == null || fromMemory.Address != graphNodeAfterReconciliation.Address) {
            throw new UnhandledCfgDiscrepancyException(
                "Nodes from memory and from graph don't have the same address. This should never happen. " +
                $"From memory: {fromMemory} " +
                $"From graph: {graphNodeAfterReconciliation}");
        }

        // Genuinely different instructions at the same address. Inject a SelectorNode
        return _nodeLinker.CreateSelectorNodeBetween(fromMemory, (CfgInstruction)graphNodeAfterReconciliation);
    }
}