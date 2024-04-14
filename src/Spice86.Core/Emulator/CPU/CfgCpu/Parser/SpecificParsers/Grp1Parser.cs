namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp1;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp1Parser : BaseInstructionParser {
    private readonly ReflectionHelper _reflectionHelper = new();
    public Grp1Parser(BaseInstructionParser instructionParser) : base(instructionParser) {
    }

    public CfgInstruction Parse(SegmentedAddress address,
        InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes,
        bool hasOperandSize32,
        int addressSizeFromPrefixes,
        uint? segmentOverrideFromPrefixes) {
        ModRmContext modRmContext = _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes);
        byte opCode = opcodeField.Value;
        bool hasOperandSize8 = opCode is 0x80 or 0x82;
        bool signExtendOp2 = opCode is 0x83;
        uint groupIndex = modRmContext.RegisterIndex;
        if (groupIndex > 7) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }

        int operandSize = _reflectionHelper.GetOperandSize(hasOperandSize8, hasOperandSize32);
        string operationName = GetOperationName(groupIndex) + GetSignedSuffix(hasOperandSize8, signExtendOp2);

        if (hasOperandSize8) {
            InstructionField<byte> valueField = _instructionReader.UInt8.NextField(false);
            return _reflectionHelper.BuildInstruction("Grp1", operationName, operandSize, address, opcodeField, prefixes, modRmContext, valueField);
        }

        if (hasOperandSize32) {
            if (signExtendOp2) {
                InstructionField<sbyte> valueField = _instructionReader.Int8.NextField(false);
                return _reflectionHelper.BuildInstruction("Grp1", operationName, operandSize, address, opcodeField, prefixes, modRmContext, valueField);
            } else {
                InstructionField<uint> valueField = _instructionReader.UInt32.NextField(false);
                return _reflectionHelper.BuildInstruction("Grp1", operationName, operandSize, address, opcodeField, prefixes, modRmContext, valueField);
            }
        }

        if (signExtendOp2) {
            InstructionField<sbyte> valueField = _instructionReader.Int8.NextField(false);
            return _reflectionHelper.BuildInstruction("Grp1", operationName, operandSize, address, opcodeField, prefixes, modRmContext, valueField);
        } else {
            InstructionField<ushort> valueField = _instructionReader.UInt16.NextField(false);
            return _reflectionHelper.BuildInstruction("Grp1", operationName, operandSize, address, opcodeField, prefixes, modRmContext, valueField);
        }
    }
    private string GetOperationName(uint groupIndex) {
        return groupIndex switch {
            0 => "Grp1Add",
            1 => "Grp1Or",
            2 => "Grp1Adc",
            3 => "Grp1Sbb",
            4 => "Grp1And",
            5 => "Grp1Sub",
            6 => "Grp1Xor",
            7 => "Grp1Cmp",
        };
    }

    private string GetSignedSuffix(bool hasOperandSize8, bool signExtendOp2) {
        if (hasOperandSize8) {
            return "";
        }
        return signExtendOp2 ? "Signed" : "Unsigned";
    }
}