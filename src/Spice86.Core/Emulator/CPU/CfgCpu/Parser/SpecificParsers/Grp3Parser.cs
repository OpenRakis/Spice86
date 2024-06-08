namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp3Parser : BaseInstructionParser {
    public Grp3Parser(BaseInstructionParser instructionParser) : base(instructionParser) {
    }

    public CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        bool hasOperandSize32,
        BitWidth addressWidthFromPrefixes,
        uint? segmentOverrideFromPrefixes) {
        ModRmContext modRmContext = _modRmParser.ParseNext(addressWidthFromPrefixes, segmentOverrideFromPrefixes);
        uint groupIndex = modRmContext.RegisterIndex;
        if (groupIndex > 7) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }

        BitWidth bitWidth = GetBitWidth(opcodeField, hasOperandSize32);
        InstructionWithModRmParser operationParser = GetOperationParser(groupIndex);
        return operationParser.Parse(address, opcodeField, prefixes, modRmContext, bitWidth);
    }

    private InstructionWithModRmParser GetOperationParser(uint groupIndex) {
        return groupIndex switch {
            0 => new Grp3TestInstructionWithModRmParser(this),
            2 => new Grp3NotRmOperationParser(this),
            3 => new Grp3NegRmOperationParser(this),
            4 => new Grp3MulRmAccOperationParser(this),
            5 => new Grp3ImulRmAccOperationParser(this),
            6 => new Grp3DivRmAccOperationParser(this),
            7 => new Grp3IdivRmAccOperationParser(this),
            _ => throw new InvalidGroupIndexException(_state, groupIndex)
        };
    }
}

public class Grp3TestInstructionWithModRmParser : BaseInstructionParser,InstructionWithModRmParser {
    public Grp3TestInstructionWithModRmParser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction Parse(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext, BitWidth bitWidth) {
        return bitWidth switch {
            BitWidth.BYTE_8 => new Grp3TestRmImm8(address, opcodeField, prefixes, modRmContext,
                _instructionReader.UInt8.NextField(false)),
            BitWidth.WORD_16 => new Grp3TestRmImm16(address, opcodeField, prefixes, modRmContext,
                _instructionReader.UInt16.NextField(false)),
            BitWidth.DWORD_32 => new Grp3TestRmImm32(address, opcodeField, prefixes, modRmContext,
                _instructionReader.UInt32.NextField(false)),
        };
    }

}

/// <summary>
/// Internal base class for generated code that will instantiate one of the grp3 operation in 8,16,32 bits mode, signed / unsigned
/// Doesn't handle TEST which has a different grammar...
/// </summary>
public abstract class Grp3OperationParser : BaseInstructionParser, InstructionWithModRmParser {
    protected Grp3OperationParser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction Parse(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext, BitWidth bitWidth) {
        return bitWidth switch {
            BitWidth.BYTE_8 => BuildOperandSize8(address, opcodeField, prefixes, modRmContext),
            BitWidth.WORD_16 => BuildOperandSize16(address, opcodeField, prefixes, modRmContext),
            BitWidth.DWORD_32 => BuildOperandSize32(address, opcodeField, prefixes, modRmContext),
        };
    }

    protected abstract CfgInstruction BuildOperandSize8(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext);
    
    protected abstract CfgInstruction BuildOperandSize16(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext);
    protected abstract CfgInstruction BuildOperandSize32(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext);
}

[Grp3OperationParser("NotRm")]
public partial class Grp3NotRmOperationParser;

[Grp3OperationParser("NegRm")]
public partial class Grp3NegRmOperationParser;

[Grp3OperationParser("MulRmAcc")]
public partial class Grp3MulRmAccOperationParser;

[Grp3OperationParser("ImulRmAcc")]
public partial class Grp3ImulRmAccOperationParser;

[Grp3OperationParser("DivRmAcc")]
public partial class Grp3DivRmAccOperationParser;

[Grp3OperationParser("IdivRmAcc")]
public partial class Grp3IdivRmAccOperationParser;