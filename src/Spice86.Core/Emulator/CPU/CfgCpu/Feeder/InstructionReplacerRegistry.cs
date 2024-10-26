namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class InstructionReplacerRegistry : IInstructionReplacer {
    private List<InstructionReplacer> _replacers = new();

    public void Register(InstructionReplacer replacer) {
        _replacers.Add(replacer);
    }

    public void ReplaceInstruction(CfgInstruction old, CfgInstruction newInstruction) {
        foreach (InstructionReplacer instructionReplacer in _replacers) {
            instructionReplacer.ReplaceInstruction(old, newInstruction);
        }
    }
}