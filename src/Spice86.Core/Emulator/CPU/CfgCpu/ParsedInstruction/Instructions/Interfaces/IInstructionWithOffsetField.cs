namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

public interface IInstructionWithOffsetField {
    public InstructionField<ushort> OffsetField { get; }
}