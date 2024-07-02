namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

public interface IInstructionWithSegmentRegisterIndex {
    public int SegmentRegisterIndex { get; }
}