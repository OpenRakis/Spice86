namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

public class ModRmParser {
    private readonly InstructionReader _instructionReader;

    public ModRmParser(InstructionReader instructionReader) {
        _instructionReader = instructionReader;
    }

    public ModRmFields ParseNextAsModRmField() {
        InstructionField<byte> modRmByte = _instructionReader.UInt8.NextField(true);
        return new ModRmFields(modRmByte, null);
    }
}