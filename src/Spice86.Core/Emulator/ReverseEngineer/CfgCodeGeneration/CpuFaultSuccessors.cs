namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

internal static class CpuFaultSuccessors {
    /// <summary>Whether <paramref name="node"/> is an instruction that can fault into a handler.</summary>
    public static bool HasCpuFaultSuccessor(ICfgNode node) =>
        node is CfgInstruction instruction
        && instruction.SuccessorsPerType.TryGetValue(InstructionSuccessorType.CpuFault, out ISet<ICfgNode>? successors)
        && successors.Count > 0;
}
