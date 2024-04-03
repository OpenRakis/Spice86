namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

using System.Numerics;

public interface IInstructionWithValueField<T> where T : INumberBase<T> {
    public InstructionField<T> ValueField { get; }
}