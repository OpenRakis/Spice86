namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;

/// <summary>
/// Common interface for instructions that implement a function return 
/// </summary>
public interface IReturnInstruction : ICfgInstruction {
    public CfgInstruction? CurrentCorrespondingCallInstruction { get; set; }
}