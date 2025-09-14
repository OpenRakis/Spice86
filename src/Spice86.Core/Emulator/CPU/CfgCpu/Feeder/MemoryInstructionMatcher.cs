namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory;

using System.Linq;

public class MemoryInstructionMatcher {
    private readonly IMemory _memory;

    public MemoryInstructionMatcher(IMemory memory) {
        this._memory = memory;
    }

    public CfgInstruction? MatchExistingInstructionWithMemory(IEnumerable<CfgInstruction> instructions) {
        return instructions.FirstOrDefault(i => IsMatchingWithCurrentMemory(i));
    }

    private bool IsMatchingWithCurrentMemory(CfgInstruction instruction) {
        IList<byte> bytesInMemory = _memory.GetSlice((int)instruction.Address.Linear, instruction.Length);
        return instruction.Signature.ListEquivalent(bytesInMemory);
    }

}