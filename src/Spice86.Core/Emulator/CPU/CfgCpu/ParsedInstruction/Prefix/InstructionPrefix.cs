namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix; 

public class InstructionPrefix {
    public InstructionPrefix(InstructionField<byte> prefixField) {
        PrefixField = prefixField;
    }

    public InstructionField<byte> PrefixField { get; }
}