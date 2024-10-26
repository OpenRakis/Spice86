namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public interface IInstructionReplacer {
    void ReplaceInstruction(CfgInstruction old, CfgInstruction instruction);
}