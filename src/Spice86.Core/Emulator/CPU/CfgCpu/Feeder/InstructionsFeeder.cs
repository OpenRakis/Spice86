namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

using System.Runtime.CompilerServices;

/// <summary>
/// Responsible for getting parsed instructions at a given address from memory.
/// Instructions are parsed only once per address, after that they are retrieved from a cache
/// Self modifying code is detected and supported.
/// If an instruction is modified and then put back in its original version, it is the original instance of the parsed instruction that will be returned for the address
/// Instructions can be replaced in the cache (see method ReplaceInstruction)
/// </summary>
public class InstructionsFeeder {
    private readonly InstructionParser _instructionParser;
    private readonly DiscriminatorReducer _discriminatorReducer;

    public InstructionsFeeder(EmulatorBreakpointsManager emulatorBreakpointsManager, IMemory memory, State cpuState,
        InstructionReplacerRegistry replacerRegistry) {
        _instructionParser = new(memory, cpuState);
        CurrentInstructions = new(memory, emulatorBreakpointsManager, replacerRegistry);
        PreviousInstructions = new(memory, replacerRegistry);
        _discriminatorReducer = new(replacerRegistry);
    }
    
    public CurrentInstructions CurrentInstructions { get; }
    public PreviousInstructions PreviousInstructions { get; }

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

        return ParseAndSetAsCurrent(address);
    }
    private CfgInstruction ParseAndSetAsCurrent(SegmentedAddress address) {
        CfgInstruction parsed = ParseEnsuringUnique(address);
        CurrentInstructions.SetAsCurrent(parsed);
        PreviousInstructions.AddInstructionInPrevious(parsed);
        return parsed;
    }

    private CfgInstruction ParseEnsuringUnique(SegmentedAddress address) {
        CfgInstruction parsed = _instructionParser.ParseInstructionAt(address);
        // Let's try with the discriminator reducer to see if the parsed instruction has an existing match
        HashSet<CfgInstruction>? previousSet = PreviousInstructions.GetAtAddress(address);
        if (previousSet != null) {
            foreach (CfgInstruction existing in previousSet) {
                CfgInstruction? reduced = _discriminatorReducer.ReduceToOne(parsed, existing);
                if (reduced != null) {
                    return reduced;
                }
            }
        }
        return parsed;
    }
}