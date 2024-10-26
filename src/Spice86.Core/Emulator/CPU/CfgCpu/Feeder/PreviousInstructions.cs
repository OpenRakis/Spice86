namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Cache of previous instructions that existed in a memory address at a time.
/// </summary>
public class PreviousInstructions : InstructionReplacer {
    private readonly MemoryInstructionMatcher _memoryInstructionMatcher;

    /// <summary>
    /// Instructions that were parsed at a given address. List for address is ordered by instruction decreasing length
    /// </summary>
    private readonly Dictionary<SegmentedAddress, HashSet<CfgInstruction>> _previousInstructionsAtAddress = new();

    public PreviousInstructions(IMemory memory, InstructionReplacerRegistry replacerRegistry) : base(
        replacerRegistry) {
        _memoryInstructionMatcher = new MemoryInstructionMatcher(memory);
    }

    public CfgInstruction? GetAtAddressIfMatchesMemory(SegmentedAddress address) {
        if (_previousInstructionsAtAddress.TryGetValue(address,
                out HashSet<CfgInstruction>? previousInstructionsAtAddress)) {
            return _memoryInstructionMatcher.MatchExistingInstructionWithMemory(previousInstructionsAtAddress);
        }

        return null;
    }

    public override void ReplaceInstruction(CfgInstruction old, CfgInstruction instruction) {
        SegmentedAddress instructionAddress = instruction.Address;

        if (_previousInstructionsAtAddress.TryGetValue(instructionAddress,
                out HashSet<CfgInstruction>? previousInstructionsAtAddress) 
            && previousInstructionsAtAddress.Remove(old)) {
            AddInstructionInPrevious(instruction);
        }
    }

    public void AddInstructionInPrevious(CfgInstruction instruction) {
        SegmentedAddress instructionAddress = instruction.Address;

        if (!_previousInstructionsAtAddress.TryGetValue(instructionAddress,
                out HashSet<CfgInstruction>? previousInstructionsAtAddress)) {
            previousInstructionsAtAddress = new HashSet<CfgInstruction>();
            _previousInstructionsAtAddress.Add(instructionAddress, previousInstructionsAtAddress);
        }

        previousInstructionsAtAddress.Add(instruction);
    }
}