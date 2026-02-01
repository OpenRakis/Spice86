namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

public class ExecutionAddressesExtractor(CfgCpu cfgCpu, ExecutionAddresses previousAddresses) {
    public ExecutionAddresses Extract() {
        // List all instructions currently in memory. Those info are meant to go along with the memory dump.
        IEnumerable<CfgInstruction> all = cfgCpu.CfgNodeFeeder.InstructionsFeeder.CurrentInstructions.GetAll();
        foreach (CfgInstruction instruction in all) {
            switch (instruction) {
                case IJumpInstruction:
                    FillResultWithSuccessorsOfType(InstructionSuccessorType.Normal, instruction, true,
                        previousAddresses.JumpsFromTo);
                    break;
                case ICallInstruction:
                    FillResultWithSuccessorsOfType(InstructionSuccessorType.Normal, instruction, false,
                        previousAddresses.CallsFromTo);
                    break;
                case IReturnInstruction:
                    FillResultWithSuccessorsOfType(InstructionSuccessorType.Normal, instruction, false,
                        previousAddresses.RetsFromTo);
                    break;
            }

            FillResultWithSuccessorsOfType(InstructionSuccessorType.CpuFault, instruction, false,
                previousAddresses.CallsFromTo);
            previousAddresses.ExecutedInstructions.Add(instruction.Address);
        }

        return previousAddresses;
    }

    private void FillResultWithSuccessorsOfType(InstructionSuccessorType type, CfgInstruction instruction,
        bool ignoreNextInMemory, IDictionary<uint, HashSet<SegmentedAddress>> result) {
        if (instruction.SuccessorsPerType.TryGetValue(type, out ISet<ICfgNode>? successors)) {
            FillResultWithSuccessors(instruction, successors, ignoreNextInMemory, result);
        }
    }

    private void FillResultWithSuccessors(CfgInstruction instruction, ISet<ICfgNode> successors,
        bool ignoreNextInMemory, IDictionary<uint, HashSet<SegmentedAddress>> result) {
        ISet<ICfgNode> filteredSuccessors = FilterSuccessors(instruction, successors, ignoreNextInMemory);
        if (filteredSuccessors.Count == 0) {
            return;
        }

        if (!result.TryGetValue(instruction.Address.Linear, out HashSet<SegmentedAddress>? set)) {
            set = new();
            result[instruction.Address.Linear] = set;
        }

        foreach (ICfgNode successor in filteredSuccessors) {
            set.Add(successor.Address);
        }
    }

    private ISet<ICfgNode> FilterSuccessors(CfgInstruction instruction, ISet<ICfgNode> successors,
        bool ignoreNextInMemory) {
        if (!ignoreNextInMemory) {
            return successors;
        }

        SegmentedAddress next = instruction.NextInMemoryAddress;
        return successors.Where(i => i.Address != next).ToHashSet();
    }
}