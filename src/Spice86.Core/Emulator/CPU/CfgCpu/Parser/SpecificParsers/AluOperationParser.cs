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
/// This class Builds the instruction class instance via reflection.
/// </summary>
public class AluOperationParser : BaseInstructionParser {
    private const byte ModRmMask = 0b100;
    private const byte SizeMask = 0b1;
    private const byte RmRegDirectionMask = 0b10;
    private readonly string _aluOperation;
    private readonly ReflectionHelper _reflectionHelper;

    public AluOperationParser(BaseInstructionParser other, string aluOperation) : base(other) {
        _aluOperation = aluOperation;
        _reflectionHelper = new ReflectionHelper();
    }

    public CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        int addressSizeFromPrefixes,
        uint? segmentOverrideFromPrefixes,
        bool hasOperandSize32) {
        byte opcode = opcodeField.Value;
        bool hasModRm = (opcode & ModRmMask) == 0;
        bool hasOperandSize8 = (opcode & SizeMask) == 0;
        int size = _reflectionHelper.GetOperandSize(hasOperandSize8, hasOperandSize32);


        if (hasModRm) {
            ModRmContext modRmContext = _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes);
            bool rmReg = (opcode & RmRegDirectionMask) == 0;
            string memoryOperation = rmReg ? "RmReg" : "RegRm";
            return _reflectionHelper.BuildInstruction($"{_aluOperation}{memoryOperation}", size, address, opcodeField, prefixes, modRmContext);
        }
        string operation = $"{_aluOperation}AccImm";
        if (hasOperandSize8) {
            return _reflectionHelper.BuildInstruction(operation, size, address, opcodeField, prefixes, _instructionReader.UInt8.NextField(false));
        }
        if (hasOperandSize32) {
            return _reflectionHelper.BuildInstruction(operation, size, address, opcodeField, prefixes, _instructionReader.UInt32.NextField(false));
        }
        return _reflectionHelper.BuildInstruction(operation, size, address, opcodeField, prefixes, _instructionReader.UInt16.NextField(false));
    }

}