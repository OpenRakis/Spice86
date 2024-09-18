namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public abstract class InstructionReplacer : IInstructionReplacer {
    protected InstructionReplacer(InstructionReplacerRegistry replacerRegistry) {
        replacerRegistry.Register(this);
    }
    public abstract void ReplaceInstruction(CfgInstruction oldInstruction, CfgInstruction newInstruction);
}