namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AddRegRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.AddRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Grp1;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.JmpNearImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovMoffsAcc;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRegImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRmImm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.MovRmReg;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.PushPop;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Registers;
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
                return new AddRmReg8(address, opcodeField, prefixes,
                    _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
            case 0x01:
                if (hasOperandSize32) {
                    return new AddRmReg32(address, opcodeField, prefixes,
                        _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
                }

                return new AddRmReg16(address, opcodeField, prefixes,
                    _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
            case 0x02:
                return new AddRegRm8(address, opcodeField, prefixes,
                    _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
            case 0x03:
                if (hasOperandSize32) {
                    return new AddRegRm32(address, opcodeField, prefixes,
                        _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
                }

                return new AddRegRm16(address, opcodeField, prefixes,
                    _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes));
            case 0x04:
                return new AddAccImm8(address, opcodeField, prefixes, _instructionReader.UInt8.NextField(false));
            case 0x05:
                if (hasOperandSize32) {
                    return new AddAccImm32(address, opcodeField, prefixes, _instructionReader.UInt32.NextField(false));
                }

                return new AddAccImm16(address, opcodeField, prefixes, _instructionReader.UInt16.NextField(false));
            case 0x80:
            case 0x81:
            case 0x82:
            case 0x83:
                return Grp1(address, opcodeField, prefixes, hasOperandSize32, addressSizeFromPrefixes,
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
                    return new Pushf32(address, opcodeField, prefixes);
                }
                return new Pushf16(address, opcodeField, prefixes);
            case 0x9D:
                if (hasOperandSize32) {
                    return new Popf32(address, opcodeField, prefixes);
                }
                return new Popf16(address, opcodeField, prefixes);
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
                return MovRegImm(address, opcodeField, prefixes, hasOperandSize32);
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

    private CfgInstruction Grp1(SegmentedAddress address, InstructionField<byte> opcodeField,
        List<InstructionPrefix> prefixes, bool hasOperandSize32, int addressSizeFromPrefixes, uint? segmentOverrideFromPrefixes) {
        ModRmContext modRmContext = _modRmParser.ParseNext(addressSizeFromPrefixes, segmentOverrideFromPrefixes);
        byte opCode = opcodeField.Value;
        bool hasOperandSize8 = opCode is 0x80 or 0x82;
        bool signExtendOp2 = opCode is 0x83;
        uint groupIndex = modRmContext.RegisterIndex;
        if (groupIndex > 7) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }
        if (hasOperandSize8) {
            InstructionField<byte> valueField = _instructionReader.UInt8.NextField(false);
            return groupIndex switch {
                0 => new Grp1Add8(address, opcodeField, prefixes, modRmContext, valueField),
                1 => new Grp1Or8(address, opcodeField, prefixes, modRmContext, valueField),
                2 => new Grp1Adc8(address, opcodeField, prefixes, modRmContext, valueField),
                3 => new Grp1Sbb8(address, opcodeField, prefixes, modRmContext, valueField),
                4 => new Grp1And8(address, opcodeField, prefixes, modRmContext, valueField),
                5 => new Grp1Sub8(address, opcodeField, prefixes, modRmContext, valueField),
                6 => new Grp1Xor8(address, opcodeField, prefixes, modRmContext, valueField),
                7 => new Grp1Cmp8(address, opcodeField, prefixes, modRmContext, valueField)
            };
        }
        if (hasOperandSize32) {
            if (signExtendOp2) {
                InstructionField<sbyte> valueField = _instructionReader.Int8.NextField(false);
                return groupIndex switch {
                    0 => new Grp1AddSigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    1 => new Grp1OrSigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    2 => new Grp1AdcSigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    3 => new Grp1SbbSigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    4 => new Grp1AndSigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    5 => new Grp1SubSigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    6 => new Grp1XorSigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    7 => new Grp1CmpSigned32(address, opcodeField, prefixes, modRmContext, valueField)
                };
            } else {
                InstructionField<uint> valueField = _instructionReader.UInt32.NextField(false);
                return groupIndex switch {
                    0 => new Grp1AddUnsigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    1 => new Grp1OrUnsigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    2 => new Grp1AdcUnsigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    3 => new Grp1SbbUnsigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    4 => new Grp1AndUnsigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    5 => new Grp1SubUnsigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    6 => new Grp1XorUnsigned32(address, opcodeField, prefixes, modRmContext, valueField),
                    7 => new Grp1CmpUnsigned32(address, opcodeField, prefixes, modRmContext, valueField)
                };
            }
        }
        if (signExtendOp2) {
            InstructionField<sbyte> valueField = _instructionReader.Int8.NextField(false);
            return groupIndex switch {
                0 => new Grp1AddSigned16(address, opcodeField, prefixes, modRmContext, valueField),
                1 => new Grp1OrSigned16(address, opcodeField, prefixes, modRmContext, valueField),
                2 => new Grp1AdcSigned16(address, opcodeField, prefixes, modRmContext, valueField),
                3 => new Grp1SbbSigned16(address, opcodeField, prefixes, modRmContext, valueField),
                4 => new Grp1AndSigned16(address, opcodeField, prefixes, modRmContext, valueField),
                5 => new Grp1SubSigned16(address, opcodeField, prefixes, modRmContext, valueField),
                6 => new Grp1XorSigned16(address, opcodeField, prefixes, modRmContext, valueField),
                7 => new Grp1CmpSigned16(address, opcodeField, prefixes, modRmContext, valueField)
            };
        } else {
            InstructionField<ushort> valueField = _instructionReader.UInt16.NextField(false);
            return groupIndex switch {
                0 => new Grp1AddUnsigned16(address, opcodeField, prefixes, modRmContext, valueField),
                1 => new Grp1OrUnsigned16(address, opcodeField, prefixes, modRmContext, valueField),
                2 => new Grp1AdcUnsigned16(address, opcodeField, prefixes, modRmContext, valueField),
                3 => new Grp1SbbUnsigned16(address, opcodeField, prefixes, modRmContext, valueField),
                4 => new Grp1AndUnsigned16(address, opcodeField, prefixes, modRmContext, valueField),
                5 => new Grp1SubUnsigned16(address, opcodeField, prefixes, modRmContext, valueField),
                6 => new Grp1XorUnsigned16(address, opcodeField, prefixes, modRmContext, valueField),
                7 => new Grp1CmpUnsigned16(address, opcodeField, prefixes, modRmContext, valueField)
            };
        }
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