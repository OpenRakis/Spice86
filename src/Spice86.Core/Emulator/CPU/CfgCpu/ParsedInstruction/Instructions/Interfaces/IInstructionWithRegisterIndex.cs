namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

public interface IInstructionWithRegisterIndex {
    public int RegisterIndex { get; }
}