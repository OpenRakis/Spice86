namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public enum InstructionSuccessorType {
    Normal,
    CallToReturn,
    CallToMisalignedReturn
}