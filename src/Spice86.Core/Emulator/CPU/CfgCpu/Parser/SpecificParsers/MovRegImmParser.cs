namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Shared.Emulator.Memory;

public class MovRegImmParser : BaseInstructionParser {
    public MovRegImmParser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction ParseMovRegImm(ParsingContext context) {
        int regIndex = ComputeRegIndex(context.OpcodeField);
        bool is8 = (context.OpcodeField.Value & WordMask) == 0;
        BitWidth bitWidth = GetBitWidth(is8, context.HasOperandSize32);
        return bitWidth switch {
            BitWidth.BYTE_8 => new MovRegImm8(context.Address, context.OpcodeField, context.Prefixes, _instructionReader.UInt8.NextField(false), regIndex),
            BitWidth.WORD_16 => new MovRegImm16(context.Address, context.OpcodeField, context.Prefixes, _instructionReader.UInt16.NextField(false), regIndex),
            BitWidth.DWORD_32 => new MovRegImm32(context.Address, context.OpcodeField, context.Prefixes, _instructionReader.UInt32.NextField(false), regIndex)
        };
    }
}