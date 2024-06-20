namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

public interface IInstructionWithOffsetField<T> {
    public InstructionField<T> OffsetField { get; }
}