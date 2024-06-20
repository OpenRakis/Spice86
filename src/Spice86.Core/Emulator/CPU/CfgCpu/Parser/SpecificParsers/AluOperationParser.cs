namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parses memory bytes into several variants of one Alu operation.
/// For one Alu operation, there are several instructions that can:
///  - perform the operation in 8, 16 or 32 bits
///  - perform the operation with ModRm writing to Reg or RegMem
///  - perform the operation with the accumulator (AL / AX / EAX) and an immediate value
/// Patterns in opcode:
/// xxxxx000 rm reg 8
/// xxxxx001 rm reg 16/32
/// xxxxx010 reg rm 8
/// xxxxx011 reg rm 16/32
/// xxxxx100 acc imm 8
/// xxxxx101 acc imm 16/32
/// </summary>

public abstract class AluOperationParser : BaseInstructionParser {
    private const byte ModRmMask = 0b100;
    private const byte RmRegDirectionMask = 0b10;

    public AluOperationParser(BaseInstructionParser other) : base(other) {
    }
    
    public CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        BitWidth addressWidthFromPrefixes,
        uint? segmentOverrideFromPrefixes,
        bool hasOperandSize32) {
        byte opcode = opcodeField.Value;
        bool hasModRm = (opcode & ModRmMask) == 0;
        BitWidth bitWidth = GetBitWidth(opcodeField, hasOperandSize32);

        if (hasModRm) {
            ModRmContext modRmContext = _modRmParser.ParseNext(addressWidthFromPrefixes, segmentOverrideFromPrefixes);
            bool rmReg = (opcode & RmRegDirectionMask) == 0;
            if (rmReg) {
                return BuildRmReg(address, opcodeField, prefixes, bitWidth, modRmContext);
            }
            return BuildRegRm(address, opcodeField, prefixes, bitWidth, modRmContext);
        }
        return BuildAccImm(address, opcodeField, prefixes, bitWidth);
    }

    protected abstract CfgInstruction BuildAccImm(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, BitWidth bitWidth);

    protected abstract CfgInstruction BuildRegRm(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, BitWidth bitWidth, ModRmContext modRmContext);

    protected abstract CfgInstruction BuildRmReg(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, BitWidth bitWidth, ModRmContext modRmContext);
}

[AluOperationParser("Add")]
public partial class AddAluOperationParser;
[AluOperationParser("Or")]
public partial class OrAluOperationParser;
[AluOperationParser("Adc")]
public partial class AdcAluOperationParser;
[AluOperationParser("Sbb")]
public partial class SbbAluOperationParser;
[AluOperationParser("And")]
public partial class AndAluOperationParser;
[AluOperationParser("Sub")]
public partial class SubAluOperationParser;
[AluOperationParser("Xor")]
public partial class XorAluOperationParser;
[AluOperationParser("Cmp")]
public partial class CmpAluOperationParser;