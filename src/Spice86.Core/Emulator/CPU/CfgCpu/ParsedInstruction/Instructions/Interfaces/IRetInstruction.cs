namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

public interface IRetInstruction : ICfgInstruction {
    public CfgInstruction? CurrentCorrespondingCallInstruction { get; set; }
}