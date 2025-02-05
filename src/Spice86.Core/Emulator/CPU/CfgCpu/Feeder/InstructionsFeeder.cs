namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Responsible for getting parsed instructions at a given address from memory.
/// Instructions are parsed only once per address, after that they are retrieved from a cache
/// Self modifying code is detected and supported.
/// If an instruction is modified and then put back in its original version, it is the original instance of the parsed instruction that will be returned for the address
/// Instructions can be replaced in the cache (see method ReplaceInstruction)
/// </summary>
public class InstructionsFeeder {
    private readonly InstructionParser _instructionParser;
    private readonly CurrentInstructions _currentInstructions;
    private readonly PreviousInstructions _previousInstructions;

    public InstructionsFeeder(EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory, State cpuState,
        InstructionReplacerRegistry replacerRegistry) {
        _currentInstructions = new(memory, emulatorBreakpointsManager, replacerRegistry);
        _instructionParser = new(memory, cpuState);
        _previousInstructions = new(memory, replacerRegistry);
    }

    public CfgInstruction GetInstructionFromMemory(SegmentedAddress address) {
        // Try to get instruction from cache that represents current memory state.
        CfgInstruction? current = _currentInstructions.GetAtAddress(address);
        if (current != null) {
            return current;
        }

        // Instruction was not in cache.
        // Either it has never been parsed or it has been evicted from cache due to self modifying code.
        // First try to see if it has been encountered before at this address instead of re-parsing.
        // Reason is we don't want several versions of the same instructions hanging around in the graph,
        // this would be bad for successors / predecessors management and self modifying code detection.
        CfgInstruction? previousMatching = _previousInstructions.GetAtAddressIfMatchesMemory(address);
        if (previousMatching != null) {
            _currentInstructions.SetAsCurrent(previousMatching);
            return previousMatching;
        }

        CfgInstruction parsed = _instructionParser.ParseInstructionAt(address);
        _currentInstructions.SetAsCurrent(parsed);
        _previousInstructions.AddInstructionInPrevious(parsed);
        return parsed;
    }
}