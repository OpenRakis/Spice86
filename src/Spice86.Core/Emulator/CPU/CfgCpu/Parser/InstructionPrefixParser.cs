namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;

public class InstructionPrefixParser {
    private readonly InstructionReader _instructionReader;

    public InstructionPrefixParser(InstructionReader instructionReader) {
        _instructionReader = instructionReader;
    }

    public InstructionPrefix? ParseNextPrefix() {
        InstructionField<byte> field = _instructionReader.UInt8.PeekField(true);
        byte prefixByte = field.Value;
        InstructionPrefix? res = prefixByte switch {
            0x26 => new SegmentOverrideInstructionPrefix(field, SegmentRegisterIndex.EsIndex),
            0x2E => new SegmentOverrideInstructionPrefix(field, SegmentRegisterIndex.CsIndex),
            0x36 => new SegmentOverrideInstructionPrefix(field, SegmentRegisterIndex.SsIndex),
            0x3E => new SegmentOverrideInstructionPrefix(field, SegmentRegisterIndex.DsIndex),
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
        if (res != null) {
            // We parsed a real prefix, move to next byte
            _instructionReader.UInt8.Advance();
        }

        return res;
    }


}