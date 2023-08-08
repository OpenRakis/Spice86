namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

public class ModRmFields {
    public InstructionField<byte> ModRm { get; }
    public InstructionField<byte>? Sib { get; }

    public ModRmFields(InstructionField<byte> modRm, InstructionField<byte>? sib) {
        ModRm = modRm;
        Sib = sib;
    }
}