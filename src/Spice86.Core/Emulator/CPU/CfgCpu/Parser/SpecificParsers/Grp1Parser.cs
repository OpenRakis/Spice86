namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp1Parser : BaseInstructionParser {
    public Grp1Parser(BaseInstructionParser instructionParser) : base(instructionParser) {
    }

    public CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        bool hasOperandSize32,
        BitWidth addressWidthFromPrefixes,
        uint? segmentOverrideFromPrefixes) {
        ModRmContext modRmContext = _modRmParser.ParseNext(addressWidthFromPrefixes, segmentOverrideFromPrefixes);
        byte opCode = opcodeField.Value;
        bool signExtendOp2 = opCode is 0x83;
        uint groupIndex = modRmContext.RegisterIndex;
        if (groupIndex > 7) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }

        BitWidth bitWidth = GetBitWidth(opcodeField, hasOperandSize32);
        Grp1OperationParser operationParser = GetOperationParser(groupIndex);
        return operationParser.Parse(address, opcodeField, prefixes, modRmContext, bitWidth, signExtendOp2);
    }

    private Grp1OperationParser GetOperationParser(uint groupIndex) {
        return groupIndex switch {
            0 => new Grp1AddOperationParser(this),
            1 => new Grp1OrOperationParser(this),
            2 => new Grp1AdcOperationParser(this),
            3 => new Grp1SbbOperationParser(this),
            4 => new Grp1AndOperationParser(this),
            5 => new Grp1SubOperationParser(this),
            6 => new Grp1XorOperationParser(this),
            7 => new Grp1CmpOperationParser(this),
        };
    }
}

/// <summary>
/// Internal base class for generated code that will instantiate one of the grp1 operation in 8,16,32 bits mode, signed / unsigned
/// </summary>
public abstract class Grp1OperationParser : BaseInstructionParser {
    protected Grp1OperationParser(BaseInstructionParser other) : base(other) {
    }

    public CfgInstruction Parse(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext, BitWidth bitWidth, bool signExtendOp2) {
        return bitWidth switch {
            BitWidth.BYTE_8 => BuildOperandSize8(address, opcodeField, prefixes, modRmContext,
                _instructionReader.UInt8.NextField(false)),
            BitWidth.WORD_16 => signExtendOp2
                ? BuildOperandSizeSigned16(address, opcodeField, prefixes, modRmContext,
                    _instructionReader.Int8.NextField(false))
                : BuildOperandSizeUnsigned16(address, opcodeField, prefixes, modRmContext,
                    _instructionReader.UInt16.NextField(false)),
            BitWidth.DWORD_32 => signExtendOp2
                ? BuildOperandSizeSigned32(address, opcodeField, prefixes, modRmContext,
                    _instructionReader.Int8.NextField(false))
                : BuildOperandSizeUnsigned32(address, opcodeField, prefixes, modRmContext,
                    _instructionReader.UInt32.NextField(false)),
        };
    }


    protected abstract CfgInstruction BuildOperandSize8(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext, InstructionField<byte> valueField);

    protected abstract CfgInstruction BuildOperandSizeSigned16(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext,
        InstructionField<sbyte> valueField);

    protected abstract CfgInstruction BuildOperandSizeUnsigned16(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext,
        InstructionField<ushort> valueField);

    protected abstract CfgInstruction BuildOperandSizeSigned32(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext,
        InstructionField<sbyte> valueField);

    protected abstract CfgInstruction BuildOperandSizeUnsigned32(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, ModRmContext modRmContext,
        InstructionField<uint> valueField);
}

[Grp1OperationParser("Add")]
public partial class Grp1AddOperationParser;

[Grp1OperationParser("Or")]
public partial class Grp1OrOperationParser;

[Grp1OperationParser("Adc")]
public partial class Grp1AdcOperationParser;

[Grp1OperationParser("Sbb")]
public partial class Grp1SbbOperationParser;

[Grp1OperationParser("And")]
public partial class Grp1AndOperationParser;

[Grp1OperationParser("Sub")]
public partial class Grp1SubOperationParser;

[Grp1OperationParser("Xor")]
public partial class Grp1XorOperationParser;

[Grp1OperationParser("Cmp")]
public partial class Grp1CmpOperationParser;