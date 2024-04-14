namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.JmpNearImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovMoffsAcc;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRmImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.PushPopF;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

public class InstructionParser  : BaseInstructionParser {
    private readonly Grp1Parser _grp1Parser;
    private readonly MovRegImmParser _movRegImmParser;
    private readonly OperationOnRegIndexParser _incRegParser;
    private readonly OperationOnRegIndexParser _decRegParser;
    private readonly OperationOnRegIndexParser _pushRegParser;
    private readonly OperationOnRegIndexParser _popRegParser;
    private readonly Grp45Parser _grp45Parser;
    private readonly AluOperationParser _addAluOperationParser;
    private readonly AluOperationParser _orAluOperationParser;
    private readonly AluOperationParser _adcAluOperationParser;
    private readonly AluOperationParser _sbbAluOperationParser;
    private readonly AluOperationParser _andAluOperationParser;
    private readonly AluOperationParser _subAluOperationParser;
    private readonly AluOperationParser _xorAluOperationParser;
    private readonly AluOperationParser _cmpAluOperationParser;

    
    public InstructionParser(IIndexable memory, State state) : base(new(memory), state) {
        _grp1Parser = new(this);
        _movRegImmParser = new(this);
        _incRegParser = new(this, "IncReg");
        _decRegParser = new(this, "DecReg");
        _pushRegParser = new(this, "PushReg");
        _popRegParser = new(this, "PopReg");
        _grp45Parser = new(this);
        _addAluOperationParser = new(this, "Add");
        _orAluOperationParser = new(this, "Or");
        _adcAluOperationParser = new(this, "Adc");
        _sbbAluOperationParser = new(this, "Sbb");
        _andAluOperationParser = new(this, "And");
        _subAluOperationParser = new(this, "Sub");
        _xorAluOperationParser = new(this, "Xor");
        _cmpAluOperationParser = new(this, "Cmp");
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
            case 0x01:
            case 0x02:
            case 0x03:
            case 0x04:
            case 0x05:
                return _addAluOperationParser.Parse(address, opcodeField, prefixes, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes, hasOperandSize32);
            case 0x08:
            case 0x09:
            case 0x0A:
            case 0x0B:
            case 0x0C:
            case 0x0D:
                return _orAluOperationParser.Parse(address, opcodeField, prefixes, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes, hasOperandSize32);
            case 0x10:
            case 0x11:
            case 0x12:
            case 0x13:
            case 0x14:
            case 0x15:
                return _adcAluOperationParser.Parse(address, opcodeField, prefixes, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes, hasOperandSize32);
            case 0x18:
            case 0x19:
            case 0x1A:
            case 0x1B:
            case 0x1C:
            case 0x1D:
                return _sbbAluOperationParser.Parse(address, opcodeField, prefixes, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes, hasOperandSize32);
            case 0x20:
            case 0x21:
            case 0x22:
            case 0x23:
            case 0x24:
            case 0x25:
                return _andAluOperationParser.Parse(address, opcodeField, prefixes, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes, hasOperandSize32);
            case 0x26:
                HandleInvalidOpcodeBecausePrefix(opcodeField.Value);
                break;
            case 0x28:
            case 0x29:
            case 0x2A:
            case 0x2B:
            case 0x2C:
            case 0x2D:
                return _subAluOperationParser.Parse(address, opcodeField, prefixes, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes, hasOperandSize32);
            case 0x2E:
                HandleInvalidOpcodeBecausePrefix(opcodeField.Value);
                break;
            case 0x30:
            case 0x31:
            case 0x32:
            case 0x33:
            case 0x34:
            case 0x35:
                return _xorAluOperationParser.Parse(address, opcodeField, prefixes, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes, hasOperandSize32);
            case 0x36:
                HandleInvalidOpcodeBecausePrefix(opcodeField.Value);
                break;
            case 0x38:
            case 0x39:
            case 0x3A:
            case 0x3B:
            case 0x3C:
            case 0x3D:
                return _cmpAluOperationParser.Parse(address, opcodeField, prefixes, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes, hasOperandSize32);
            case 0x3E:
                HandleInvalidOpcodeBecausePrefix(opcodeField.Value);
                break;
            case 0x40:
            case 0x41:
            case 0x42:
            case 0x43:
            case 0x44:
            case 0x45:
            case 0x46:
            case 0x47:
                return _incRegParser.Parse(address, opcodeField, prefixes, hasOperandSize32);
            case 0x48:
            case 0x49:
            case 0x4A:
            case 0x4B:
            case 0x4C:
            case 0x4D:
            case 0x4E:
            case 0x4F:
                return _decRegParser.Parse(address, opcodeField, prefixes, hasOperandSize32);
            case 0x50:
            case 0x51:
            case 0x52:
            case 0x53:
            case 0x54:
            case 0x55:
            case 0x56:
            case 0x57:
                return _pushRegParser.Parse(address, opcodeField, prefixes, hasOperandSize32);
            case 0x58:
            case 0x59:
            case 0x5A:
            case 0x5B:
            case 0x5C:
            case 0x5D:
            case 0x5E:
            case 0x5F:
                return _popRegParser.Parse(address, opcodeField, prefixes, hasOperandSize32);
            case 0x62:// BOUND
            case 0x63:// ARPL
                HandleInvalidOpcode(opcodeField.Value);
                break;
            case 0x64:
            case 0x65:
            case 0x66:
            case 0x67:
                HandleInvalidOpcodeBecausePrefix(opcodeField.Value);
                break;
            case 0x80:
            case 0x81:
            case 0x82:
            case 0x83:
                return _grp1Parser.Parse(address, opcodeField, prefixes, hasOperandSize32, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes);
            case 0x88:
                return new MovRmReg8(address, opcodeField, prefixes,
                    _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
            case 0x89:
                if (hasOperandSize32) {
                    return new MovRmReg32(address, opcodeField, prefixes,
                        _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
                }
                return new MovRmReg16(address, opcodeField, prefixes,
                    _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
            case 0x9C:
                if (hasOperandSize32) {
                    return new PushF32(address, opcodeField, prefixes);
                }
                return new PushF16(address, opcodeField, prefixes);
            case 0x9D:
                if (hasOperandSize32) {
                    return new PopF32(address, opcodeField, prefixes);
                }
                return new PopF16(address, opcodeField, prefixes);
            case 0xA2:
                return new MovMoffsAcc8(address, opcodeField, prefixes,
                    segmentOverrideFromPrefixes ?? SegmentRegisters.DsIndex,
                    _instructionReader.UInt16.NextField(false));
            case 0xA3:
                if (hasOperandSize32) {
                    return new MovMoffsAcc32(address, opcodeField, prefixes,
                        segmentOverrideFromPrefixes ?? SegmentRegisters.DsIndex,
                        _instructionReader.UInt16.NextField(false));
                }
                return new MovMoffsAcc16(address, opcodeField, prefixes,
                    segmentOverrideFromPrefixes ?? SegmentRegisters.DsIndex,
                    _instructionReader.UInt16.NextField(false));
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
                return _movRegImmParser.ParseMovRegImm(address, opcodeField, prefixes, hasOperandSize32);
            case 0xC6:
                return new MovRmImm8(address, opcodeField, prefixes,
                    _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes),
                    _instructionReader.UInt8.NextField(false));
            case 0xC7:
                if (hasOperandSize32) {
                    return new MovRmImm32(address, opcodeField, prefixes,
                        _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes),
                        _instructionReader.UInt32.NextField(false));
                }
                return new MovRmImm16(address, opcodeField, prefixes,
                    _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes),
                    _instructionReader.UInt16.NextField(false));
            case 0xE9: 
                return new JmpNearImm16(address, opcodeField, _instructionReader.Int16.NextField(true));
            case 0xEB: 
                return new JmpNearImm8(address, opcodeField, _instructionReader.Int8.NextField(true));
            case 0xF4:
                return new Hlt(address, opcodeField);
            case 0xFE:
            case 0xFF:
                return _grp45Parser.Parse(address, opcodeField, prefixes, addressSizeFromPrefixes,
                    segmentOverrideFromPrefixes, hasOperandSize32);
        }

        return HandleInvalidOpcode(opcodeField.Value);
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

    private void HandleInvalidOpcodeBecausePrefix(byte opcode) =>
        throw new InvalidOpCodeException(_state, opcode, true);
}