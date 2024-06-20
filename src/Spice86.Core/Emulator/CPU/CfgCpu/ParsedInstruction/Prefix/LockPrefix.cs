namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;

public class LockPrefix : InstructionPrefix {
    public LockPrefix(InstructionField<byte> prefixField) : base(prefixField) {
    }
}