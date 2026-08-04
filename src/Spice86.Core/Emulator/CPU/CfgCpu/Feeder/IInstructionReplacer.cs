namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public interface IInstructionReplacer {
    void ReplaceInstruction(CfgInstruction oldInstruction, CfgInstruction newInstruction);

    /// <summary>
    /// Removes <paramref name="instruction"/> from the subscriber's state. Inverse of
    /// <see cref="ReplaceInstruction"/>: the single fan-out point every identity-level removal is
    /// routed through, so each subscriber cleans up its own reference to the node.
    /// </summary>
    void RemoveInstruction(CfgInstruction instruction);
}
