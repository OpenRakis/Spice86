namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

public interface IInstructionWithSegmentField {
    public InstructionField<ushort> SegmentField { get; }
}