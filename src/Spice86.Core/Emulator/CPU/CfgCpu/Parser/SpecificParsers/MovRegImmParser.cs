namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class MovRegImmParser : BaseInstructionParser {
    public MovRegImmParser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction ParseMovRegImm(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, bool hasOperandSize32) {
        int regIndex = ComputeRegIndex(opcodeField);
        bool is8 = (opcodeField.Value & WordMask) == 0;
        BitWidth bitWidth = GetBitWidth(is8, hasOperandSize32);
        return bitWidth switch {
            BitWidth.BYTE_8 => new MovRegImm8(address, opcodeField, prefixes, _instructionReader.UInt8.NextField(false), regIndex),
            BitWidth.WORD_16 => new MovRegImm16(address, opcodeField, prefixes, _instructionReader.UInt16.NextField(false), regIndex),
            BitWidth.DWORD_32 => new MovRegImm32(address, opcodeField, prefixes, _instructionReader.UInt32.NextField(false), regIndex)
        };
    }
}