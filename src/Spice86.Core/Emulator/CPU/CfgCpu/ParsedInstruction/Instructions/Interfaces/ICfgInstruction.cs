namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Emulator.Memory;

public interface ICfgInstruction : ICfgNode {
    public Dictionary<SegmentedAddress, ICfgNode> SuccessorsPerAddress { get; }

}