namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Exceptions;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Handles coherency between the memory and the graph of instructions executed by the CPU.
/// Next node to execute is normally the next node from the graph but several checks are done to make sure it is really it:
///  - The node is not null (otherwise it is taken from memory)
///  - If the node is an assembly node, it is the same as what is currently in memory, otherwise it means self modifying code is being detected
///  - If self modifying code is being detected, Discriminator node is being injected instead.
/// Once the node to execute is determined, it is linked to the previously executed node in the execution context if possible.
/// </summary>
public class CfgNodeFeeder {
    private readonly State _state;
    private readonly InstructionsFeeder _instructionsFeeder;
    private readonly NodeLinker _nodeLinker;
    private readonly DiscriminatorReducer _discriminatorReducer;

    public CfgNodeFeeder(IMemory memory, State state, EmulatorBreakpointsManager emulatorBreakpointsManager,
        InstructionReplacerRegistry replacerRegistry) {
        _state = state;
        _instructionsFeeder = new(emulatorBreakpointsManager, memory, state, replacerRegistry);
        _nodeLinker = new(replacerRegistry);
        _discriminatorReducer = new(replacerRegistry);
    }

    public CfgInstruction CurrentNodeFromInstructionFeeder =>
        _instructionsFeeder.GetInstructionFromMemory(_state.IpSegmentedAddress);

    public ICfgNode GetLinkedCfgNodeToExecute(ExecutionContext executionContext) {
        // Determine actual node to execute. Graph may not represent what is actually in memory if graph is not complete or if self modifying code
        ICfgNode toExecute = DetermineToExecute(executionContext.NodeToExecuteNextAccordingToGraph);
        if (executionContext.LastExecuted != null) {
            // Register what we found in the graph
            _nodeLinker.Link(executionContext.LastExecuted, toExecute);
        }

        return toExecute;
    }

    private ICfgNode DetermineToExecute(ICfgNode? currentFromGraph) {
        if (currentFromGraph == null) {
            // Need to fetch from feeder
            return CurrentNodeFromInstructionFeeder;
        }

        if (!currentFromGraph.IsAssembly) {
            // Cannot check if match with memory. Just execute it.
            return currentFromGraph;
        }

        CfgInstruction fromMemory = CurrentNodeFromInstructionFeeder;
        if (ReferenceEquals(fromMemory, currentFromGraph)) {
            // Instruction is assembly and Graph agrees with memory. Nominal case.
            return currentFromGraph;
        }

        if (fromMemory.Address != currentFromGraph.Address) {
            // should never happen
            throw new UnhandledCfgDiscrepancyException("Nodes from memory and from graph don't have the same address. This should never happen.");
        }

        // Graph and memory are not aligned ... Need to inject Node with discriminator
        // If previous was Discriminated and current was not in its successors we would not be there because 
        // currentFromGraph would have been null and the linker would then link it to DiscriminatedNode
        return CreateDiscriminatedNode(fromMemory, (CfgInstruction)currentFromGraph);
    }

    private ICfgNode CreateDiscriminatedNode(CfgInstruction instruction1, CfgInstruction instruction2) {
        IList<CfgInstruction> reducedInstructions =
            _discriminatorReducer.ReduceAll([instruction1, instruction2]);
        if (reducedInstructions.Count == 1) {
            return reducedInstructions[0];
        }

        DiscriminatedNode res = new DiscriminatedNode(instruction1.Address);
        foreach (CfgInstruction reducedInstruction in reducedInstructions) {
            // Make predecessors of instructions point to res instead of instruction1/2
            _nodeLinker.InsertIntermediatePredecessor(reducedInstruction, res);
        }

        return res;
    }
}