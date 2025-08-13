namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using System.Linq;

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

    public List<CfgInstruction> GetAll() {
        return _previousInstructionsAtAddress.Values.SelectMany(x => x).ToList();
    }

    public HashSet<CfgInstruction>? GetAtAddress(SegmentedAddress address) {
        return _previousInstructionsAtAddress.GetValueOrDefault(address);
    }

    public CfgInstruction? GetAtAddressIfMatchesMemory(SegmentedAddress address) {
        HashSet<CfgInstruction>? previousInstructionsAtAddress = GetAtAddress(address);
        if (previousInstructionsAtAddress == null) {
            return null;
        }

        return _memoryInstructionMatcher.MatchExistingInstructionWithMemory(previousInstructionsAtAddress);
    }

    public override void ReplaceInstruction(CfgInstruction oldInstruction, CfgInstruction newInstruction) {
        SegmentedAddress instructionAddress = newInstruction.Address;

        if (_previousInstructionsAtAddress.TryGetValue(instructionAddress,
                out HashSet<CfgInstruction>? previousInstructionsAtAddress) 
            && previousInstructionsAtAddress.Remove(oldInstruction)) {
            AddInstructionInPrevious(newInstruction);
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