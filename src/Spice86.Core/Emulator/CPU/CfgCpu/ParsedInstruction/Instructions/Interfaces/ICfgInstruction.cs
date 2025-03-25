namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Emulator.Memory;

public interface ICfgInstruction : ICfgNode {
    /// <summary>
    /// Cache of Successors property per address.
    /// </summary>
    public Dictionary<SegmentedAddress, ICfgNode> SuccessorsPerAddress { get; }

    /// <summary>
    /// Successors per link type
    /// This allows to represent the link between a call instruction and the effective return address.
    /// This is present for all instructions since most of them can trigger CPU faults (and interrupt calls)
    /// </summary>
    public Dictionary<InstructionSuccessorType, ISet<ICfgNode>> SuccessorsPerType { get; }
}