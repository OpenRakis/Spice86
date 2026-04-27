namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;
using Spice86.Core.Emulator.CPU.Registers;

public class InstructionPrefixParser {
    private readonly InstructionReader _instructionReader;

    public InstructionPrefixParser(InstructionReader instructionReader) {
        _instructionReader = instructionReader;
    }

    public InstructionPrefix? ParseNextPrefix() {
        InstructionField<byte> field = _instructionReader.UInt8.PeekField(true);
        byte prefixByte = field.Value;
        InstructionPrefix? res;
        // Legacy segment overrides: hi2=0, lo3=6, mid3 encodes segment (4=ES, 5=CS, 6=SS, 7=DS → index = mid3 - 4)
        int lo3 = X86OctalParser.Lo3(prefixByte);
        int mid3 = X86OctalParser.Mid3(prefixByte);
        uint hi2 = X86OctalParser.Hi2(prefixByte);
        if (hi2 == 0 && lo3 == 6 && mid3 >= 4) {
            int segIndex = mid3 - 4;
            res = new SegmentOverrideInstructionPrefix(field, (SegmentRegisterIndex)segIndex);
        } else {
            res = prefixByte switch {
                0x64 => new SegmentOverrideInstructionPrefix(field, SegmentRegisterIndex.FsIndex),
                0x65 => new SegmentOverrideInstructionPrefix(field, SegmentRegisterIndex.GsIndex),
                0x66 => new OperandSize32Prefix(field),
                0x67 => new AddressSize32Prefix(field),
                0xF0 => new LockPrefix(field),
                // REPNZ
                0xF2 => new RepPrefix(field, false),
                // REPZ
                0xF3 => new RepPrefix(field, true),
                _ => null
            };
        }
        if (res != null) {
            // We parsed a real prefix, move to next byte
            _instructionReader.UInt8.Advance();
        }

        return res;
    }


}