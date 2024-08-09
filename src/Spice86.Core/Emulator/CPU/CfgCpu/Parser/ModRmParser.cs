namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class ModRmParser {
    private readonly InstructionReader _instructionReader;
    private readonly State _state;


    public ModRmParser(InstructionReader instructionReader, State state) {
        _instructionReader = instructionReader;
        _state = state;
    }

    public ModRmContext ParseNext(ModRmParsingContext context) {
        InstructionField<byte> modRmByteField = _instructionReader.UInt8.NextField(true);
        byte modRmByte = modRmByteField.Value;
        uint mode = (uint)(modRmByte >> 6 & 0b11);
        int registerIndex = modRmByte >> 3 & 0b111;
        int registerMemoryIndex = modRmByte & 0b111;
        MemoryOffsetType memoryOffsetType;
        MemoryAddressType memoryAddressType;
        if (mode == 3) {
            // value at reg[memoryRegisterIndex] to be used instead of memoryAddress
            memoryOffsetType = MemoryOffsetType.NONE;
            memoryAddressType = MemoryAddressType.NONE;
            return new(modRmByteField, mode, registerIndex, registerMemoryIndex, context.AddressWidthFromPrefixes, memoryOffsetType, memoryAddressType, null, null, null, null, null, null);
        }
        memoryOffsetType = MemoryOffsetType.OFFSET_PLUS_DISPLACEMENT;
        memoryAddressType = MemoryAddressType.SEGMENT_OFFSET;
        SibContext? sibContext;
        DisplacementType displacementType;
        FieldWithValue? displacementField;
        ModRmOffsetType modRmOffsetType;
        InstructionField<ushort>? modRmOffsetField = null;

        if (context.AddressWidthFromPrefixes == BitWidth.WORD_16) {
            // No SIB in 16bit addressing mode
            sibContext = null;
            // Parse displacement
            displacementType = ComputeDisplacementType16(mode);
            displacementField = ReadDisplacementField(displacementType);
            // Parse offset
            modRmOffsetType = ComputeOffset16(mode, registerMemoryIndex);
            if (modRmOffsetType == ModRmOffsetType.OFFSET_FIELD_16) {
                modRmOffsetField = _instructionReader.UInt16.NextField(false);
            }
        } else {
            modRmOffsetType = ComputeOffset32(mode, registerMemoryIndex);
            // Parse SIB
            sibContext = modRmOffsetType == ModRmOffsetType.SIB ? ParseSibContext(mode) : null;
            // Parse displacement
            displacementType = ComputeDisplacementType32(mode);
            displacementField = ReadDisplacementField(displacementType);
        }
        int segmentIndex = context.SegmentOverrideFromPrefixes ?? ComputeDefaultSegmentIndex(mode, registerMemoryIndex);
        return new(modRmByteField, mode, registerIndex, registerMemoryIndex, context.AddressWidthFromPrefixes, memoryOffsetType, memoryAddressType, sibContext,
            displacementType, displacementField, modRmOffsetType, modRmOffsetField, segmentIndex);
    }

    private SibContext ParseSibContext(uint mode) {
        InstructionField<byte> sibByteField = _instructionReader.UInt8.NextField(true);
        byte sibByte = sibByteField.Value;
        int scale = 1 << (sibByte >> 6 & 0b11);
        int indexRegister = sibByte >> 3 & 0b111;
        int baseRegister = sibByte & 0b111;
        SibBase sibBase = ComputeSibBase(baseRegister, mode);
        InstructionField<uint>? baseField = null;
        if (sibBase == SibBase.BASE_FIELD_32) {
            baseField = _instructionReader.UInt32.NextField(false);
        }
        SibIndex sibIndex = ComputeSibIndex(indexRegister);
        return new SibContext(sibByteField, scale, indexRegister, baseRegister, sibBase, baseField, sibIndex);
    }

    private DisplacementType ComputeDisplacementType16(uint mode) {
        return mode switch {
            1 => DisplacementType.INT8,
            2 => DisplacementType.INT16,
            _ => DisplacementType.ZERO
        };
    }
    private DisplacementType ComputeDisplacementType32(uint mode) {
        return mode switch {
            1 => DisplacementType.INT8,
            2 => DisplacementType.INT32,
            _ => DisplacementType.ZERO
        };
    }

    private FieldWithValue? ReadDisplacementField(DisplacementType displacementType) {
        return displacementType switch {
            DisplacementType.ZERO => null,
            DisplacementType.INT8 => _instructionReader.Int8.NextField(false),
            DisplacementType.INT16 => _instructionReader.Int16.NextField(false),
            DisplacementType.INT32 => _instructionReader.Int32.NextField(false),
        };
    }

    private int ComputeDefaultSegmentIndex(uint mode, int registerMemoryIndex) {
        // The default segment register is SS for the effective addresses containing a
        // BP index, DS for other effective addresses
        return registerMemoryIndex switch {
            0 => (int)SegmentRegisterIndex.DsIndex,
            1 => (int)SegmentRegisterIndex.DsIndex,
            2 => (int)SegmentRegisterIndex.SsIndex,
            3 => (int)SegmentRegisterIndex.SsIndex,
            4 => (int)SegmentRegisterIndex.DsIndex,
            5 => (int)SegmentRegisterIndex.DsIndex,
            6 => mode == 0 ? (int)SegmentRegisterIndex.DsIndex : (int)SegmentRegisterIndex.SsIndex,
            7 => (int)SegmentRegisterIndex.DsIndex,
            _ => throw new InvalidRegisterMemoryIndexException(_state, registerMemoryIndex)
        };
    }

    private ModRmOffsetType ComputeOffset16(uint mode, int registerMemoryIndex) {
        return registerMemoryIndex switch {
            0 => ModRmOffsetType.BX_PLUS_SI,
            1 => ModRmOffsetType.BX_PLUS_DI,
            2 => ModRmOffsetType.BP_PLUS_SI,
            3 => ModRmOffsetType.BP_PLUS_DI,
            4 => ModRmOffsetType.SI,
            5 => ModRmOffsetType.DI,
            6 => mode == 0 ? ModRmOffsetType.OFFSET_FIELD_16 : ModRmOffsetType.BP,
            7 => ModRmOffsetType.BX,
            _ => throw new InvalidRegisterMemoryIndexException(_state, registerMemoryIndex)
        };
    }

    private ModRmOffsetType ComputeOffset32(uint mode, int registerMemoryIndex) {
        return registerMemoryIndex switch {
            0 => ModRmOffsetType.EAX,
            1 => ModRmOffsetType.ECX,
            2 => ModRmOffsetType.EDX,
            3 => ModRmOffsetType.EBX,
            4 => ModRmOffsetType.SIB,
            5 => ModRmOffsetType.EBP,
            6 => ModRmOffsetType.ESI,
            7 => ModRmOffsetType.EDI,
            _ => throw new InvalidRegisterMemoryIndexException(_state, registerMemoryIndex)
        };
    }

    private SibIndex ComputeSibIndex(int indexRegister) {
        return indexRegister switch {
            0 => SibIndex.EAX,
            1 => SibIndex.ECX,
            2 => SibIndex.EDX,
            3 => SibIndex.EBX,
            4 => SibIndex.ZERO,
            5 => SibIndex.EBP,
            6 => SibIndex.ESI,
            7 => SibIndex.EDI,
            _ => throw new ArgumentOutOfRangeException(nameof(indexRegister), indexRegister, "Index register must be between 0 and 7 inclusive")
        };
    }

    private static SibBase ComputeSibBase(int baseRegister, uint mode) {
        return baseRegister switch {
            0 => SibBase.EAX,
            1 => SibBase.ECX,
            2 => SibBase.EDX,
            3 => SibBase.EBX,
            4 => SibBase.ESP,
            5 => mode == 0 ? SibBase.BASE_FIELD_32 : SibBase.EBP,
            6 => SibBase.ESI,
            7 => SibBase.EDI,
            _ => throw new ArgumentOutOfRangeException(nameof(baseRegister), baseRegister, "Base register must be between 0 and 7 inclusive")
        };
    }
    
    public ModRmContext EnsureNotMode3(ModRmContext context) {
        if (context.MemoryAddressType == MemoryAddressType.NONE) {
            throw new MemoryAddressMandatoryException(_state);
        }

        return context;
    }

}