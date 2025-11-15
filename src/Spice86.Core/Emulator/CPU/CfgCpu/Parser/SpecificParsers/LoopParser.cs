namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Shared.Emulator.Memory;

public class LoopParser(BaseInstructionParser instructionParser) : BaseInstructionParser(instructionParser) {
    public CfgInstruction Parse(ParsingContext context) {
        BitWidth addressWidth = context.AddressWidthFromPrefixes;
        ushort opcode = context.OpcodeField.Value;
        InstructionField<sbyte> offsetField = _instructionReader.Int8.NextField(true);
        if (BitIsTrue(opcode, 1)) {
            // Loop with no condition
            return addressWidth switch {
                BitWidth.WORD_16 => new Loop16(context.Address, context.OpcodeField, context.Prefixes, offsetField),
                BitWidth.DWORD_32 => new Loop32(context.Address, context.OpcodeField, context.Prefixes, offsetField),
                _ => throw CreateUnsupportedBitWidthException(addressWidth)
            };
        }
        if (BitIsTrue(opcode, 0)) {
            // Loopz (loop if zf == 1)
            return addressWidth switch {
                BitWidth.WORD_16 => new Loopz16(context.Address, context.OpcodeField, context.Prefixes, offsetField),
                BitWidth.DWORD_32 => new Loopz32(context.Address, context.OpcodeField, context.Prefixes, offsetField),
                _ => throw CreateUnsupportedBitWidthException(addressWidth)
            };
        }
        // Loopnz (loop if zf == 0)
        return addressWidth switch {
            BitWidth.WORD_16 => new Loopnz16(context.Address, context.OpcodeField, context.Prefixes, offsetField),
            BitWidth.DWORD_32 => new Loopnz32(context.Address, context.OpcodeField, context.Prefixes, offsetField),
            _ => throw CreateUnsupportedBitWidthException(addressWidth)
        };
    }

}