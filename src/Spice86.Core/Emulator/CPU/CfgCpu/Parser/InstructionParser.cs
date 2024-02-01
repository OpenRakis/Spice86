namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

public class InstructionParser {
    private const int RegIndexMask = 0b111;
    private const int WordMask = 0b1000;


    private readonly InstructionReader _instructionReader;
    private readonly InstructionPrefixParser _instructionPrefixParser;
    private readonly ModRmParser _modRmParser;
    private readonly State _state;

    public InstructionParser(IIndexable memory, State state) {
        _instructionReader = new(memory);
        _instructionPrefixParser = new(_instructionReader);
        _modRmParser = new(_instructionReader, state);
        _state = state;
    }

    public CfgInstruction ParseInstructionAt(SegmentedAddress address) {
        _instructionReader.InstructionReaderAddressSource.InstructionAddress = address;
        List<InstructionPrefix> prefixes = ParsePrefixes();
        InstructionField<byte> opcodeField = _instructionReader.UInt8.NextField(true);
        CfgInstruction res = ParseCfgInstruction(address, opcodeField, prefixes);
        res.PostInit();
        return res;
    }

    private bool HasOperandSize32(IList<InstructionPrefix> prefixes) {
        return prefixes.Where(p => p is OperandSize32Prefix).Any();
    }

    private bool HasAddressSize32(IList<InstructionPrefix> prefixes) {
        return prefixes.Where(p => p is AddressSize32Prefix).Any();
    }

    private CfgInstruction ParseCfgInstruction(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes) {
        int addressSizeFromPrefixes = ComputeAddressSize(prefixes);
        uint? segmentOverrideFromPrefixes = ComputeSegmentOverrideIndex(prefixes);
        bool hasOperandSize32 = HasOperandSize32(prefixes);
        switch (opcodeField.Value) {
            case 0x00:
                return new AddRmReg8(address, opcodeField, prefixes, _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
            case 0x01:
                if (hasOperandSize32) {
                    return new AddRmReg32(address, opcodeField, prefixes, _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
                }
                return new AddRmReg16(address, opcodeField, prefixes, _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
            case 0xB0:
            case 0xB1:
            case 0xB2:
            case 0xB3:
            case 0xB4:
            case 0xB5:
            case 0xB6:
            case 0xB7:
            case 0xB8:
            case 0xB9:
            case 0xBA:
            case 0xBB:
            case 0xBC:
            case 0xBD:
            case 0xBE:
            case 0xBF:
                return MovRegImm(address, opcodeField, prefixes, hasOperandSize32);
            case 0xE9: return new JmpNearImm16(address, opcodeField, _instructionReader.Int16.NextField(true));
            case 0xEB: return new JmpNearImm8(address, opcodeField, _instructionReader.Int8.NextField(true));
            case 0xF4: return new HltInstruction(address, opcodeField);
        }

        return HandleInvalidOpcode(opcodeField.Value);
    }

    private CfgInstruction MovRegImm(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, bool hasOperandSize32) {
        int regIndex = opcodeField.Value & RegIndexMask;
        if ((opcodeField.Value & WordMask) == 0) {
            return new MovRegImm8(address, opcodeField, prefixes, _instructionReader.UInt8.NextField(false),
                regIndex);
        }

        if (hasOperandSize32) {
            return new MovRegImm32(address, opcodeField, prefixes, _instructionReader.UInt32.NextField(false),
                regIndex);
        }

        return new MovRegImm16(address, opcodeField, prefixes, _instructionReader.UInt16.NextField(false), regIndex);
    }

    private List<InstructionPrefix> ParsePrefixes() {
        List<InstructionPrefix> res = new();
        InstructionPrefix? nextPrefix = _instructionPrefixParser.ParseNextPrefix();
        while (nextPrefix != null) {
            res.Add(nextPrefix);
            nextPrefix = _instructionPrefixParser.ParseNextPrefix();
        }

        return res;
    }

    private uint? ComputeSegmentOverrideIndex(List<InstructionPrefix> prefixes) {
        SegmentOverrideInstructionPrefix? overridePrefix = prefixes.OfType<SegmentOverrideInstructionPrefix>().FirstOrDefault();
        return overridePrefix?.SegmentRegisterIndexValue;
    }
    
    private int ComputeAddressSize(List<InstructionPrefix> prefixes) {
        AddressSize32Prefix? addressSize32Prefix = prefixes.OfType<AddressSize32Prefix>().FirstOrDefault();
        return addressSize32Prefix == null ? 16 : 32;
    }

    private CfgInstruction HandleInvalidOpcode(ushort opcode) =>
        throw new InvalidOpCodeException(_state, opcode, false);
}