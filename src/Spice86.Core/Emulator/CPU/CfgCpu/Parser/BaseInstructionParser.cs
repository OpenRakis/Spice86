namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;
using Spice86.Shared.Emulator.Memory;

public class BaseInstructionParser {
    protected const int RegIndexMask = 0b111;
    protected const int WordMask = 0b1000;
    protected readonly InstructionReader _instructionReader;
    protected readonly InstructionPrefixParser _instructionPrefixParser;
    protected readonly ModRmParser _modRmParser;
    protected readonly State _state;

    protected BaseInstructionParser(InstructionReader instructionReader, State state) {
        _instructionReader = instructionReader;
        _instructionPrefixParser =  new(_instructionReader);
        _modRmParser = new(_instructionReader, state);
        _state = state;
    }

    protected BaseInstructionParser(BaseInstructionParser other) {
        _instructionReader = other._instructionReader;
        _instructionPrefixParser = other._instructionPrefixParser;
        _modRmParser = other._modRmParser;
        _state = other._state;
    }

    protected int ComputeRegIndex(InstructionField<byte> opcodeField) {
        return opcodeField.Value & RegIndexMask;
    }
    
    protected BitWidth GetBitWidth(bool is8, bool is32) {
        return is8 ? BitWidth.BYTE_8 : is32 ? BitWidth.DWORD_32 : BitWidth.WORD_16;
    }
}