namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

public interface IInstructionReplacer<in T> where T : ICfgNode {
    void ReplaceInstruction(T old, T instruction);
}