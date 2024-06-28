namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Collections.Immutable;

public class ModRmComputer {
    private readonly State _state;
    private readonly InstructionFieldValueRetriever _instructionFieldValueRetriever;
    public ModRmComputer(State state, InstructionFieldValueRetriever instructionFieldValueRetriever) {
        _state = state;
        _instructionFieldValueRetriever = instructionFieldValueRetriever;
        // Dummy value
        ModRmContext = new ModRmContext(new InstructionField<byte>(0,0,0,0,ImmutableList.CreateRange(new []{(byte?)0})), 0, 0, 0, BitWidth.WORD_16, MemoryOffsetType.NONE, MemoryAddressType.NONE, null, null, null, null, null, null);
    }

    public ModRmContext ModRmContext { get; set; }

    
    /// <summary>
    /// Gets the linear address the ModRM byte can point at. Can be <c>null</c>.
    /// </summary>
    public uint? ComputeMemoryAddress(ushort? memoryOffset) {
        if (ModRmContext.MemoryAddressType == MemoryAddressType.NONE) {
            return null;
        }
        if (memoryOffset == null) {
            throw CreateInvalidOperationExceptionForNullFieldButModeNot3(nameof(memoryOffset));
        }
        return GetPhysicalAddress(memoryOffset.Value);
    }
    
    /// <summary>
    /// Computes a physical address from an offset and the segment register used in this modrm operation
    /// </summary>
    /// <param name="offset"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public uint GetPhysicalAddress(ushort offset) {
        if (ModRmContext.SegmentIndex == null) {
            throw CreateInvalidOperationExceptionForNullFieldButModeNot3(nameof(ModRmContext.SegmentIndex));
        }
        ushort segmentValue = _state.SegmentRegisters.UInt16[ModRmContext.SegmentIndex.Value];
        return MemoryUtils.ToPhysicalAddress(segmentValue, offset);
    }

    /// <summary>
    /// Gets the memory offset of the ModRM byte can point at. Can be <c>null</c>.
    /// </summary>
    public ushort? ComputeMemoryOffset() {
        if (ModRmContext.MemoryOffsetType == MemoryOffsetType.NONE) {
            return null;
        }
        int displacement = ComputeDisplacement();
        uint offset = ComputeOffset();
        uint res = (uint)(offset + displacement);
        if (ModRmContext.AddressSize == BitWidth.DWORD_32 && res > ushort.MaxValue) {
            throw new CpuGeneralProtectionFaultException("Displacement overflows 16 bits");
        }
        return (ushort)res;
    }

    private int ComputeDisplacement() {
        if (ModRmContext.DisplacementType == null) {
            throw CreateInvalidOperationExceptionForNullFieldButModeNot3(nameof(ModRmContext.DisplacementType));
        }
        if (ModRmContext.DisplacementType == DisplacementType.ZERO) {
            return 0;
        }
        if (ModRmContext.DisplacementField == null) {
            throw CreateInvalidOperationExceptionForNullFieldButModeNot3(nameof(ModRmContext.DisplacementField));
        }
        // Displacement is signed extended!
        return ModRmContext.DisplacementType switch {
            DisplacementType.INT8 => _instructionFieldValueRetriever.GetFieldValue((InstructionField<sbyte>)ModRmContext.DisplacementField),
            DisplacementType.INT16 => _instructionFieldValueRetriever.GetFieldValue((InstructionField<short>)ModRmContext.DisplacementField),
            DisplacementType.INT32 => _instructionFieldValueRetriever.GetFieldValue((InstructionField<int>)ModRmContext.DisplacementField),
            _ => throw new ArgumentOutOfRangeException(nameof(ModRmContext.DisplacementType), ModRmContext.DisplacementType, "value not handled")
        };
    }

    private uint ComputeOffset() {
        if (ModRmContext.ModRmOffsetType == null) {
            throw CreateInvalidOperationExceptionForNullFieldButModeNot3(nameof(ModRmContext.ModRmOffsetType));
        }
        return ModRmContext.ModRmOffsetType switch {
            ModRmOffsetType.BX_PLUS_SI => (ushort)(_state.BX + _state.SI),
            ModRmOffsetType.BX_PLUS_DI => (ushort)(_state.BX + _state.DI),
            ModRmOffsetType.BP_PLUS_SI => (ushort)(_state.BP + _state.SI),
            ModRmOffsetType.BP_PLUS_DI => (ushort)(_state.BP + _state.DI),
            ModRmOffsetType.SI => _state.SI,
            ModRmOffsetType.DI => _state.DI,
            ModRmOffsetType.OFFSET_FIELD_16 => GetOffsetFieldValue(),
            ModRmOffsetType.BP => _state.BP,
            ModRmOffsetType.BX => _state.BX,
            ModRmOffsetType.EAX => _state.EAX,
            ModRmOffsetType.ECX => _state.ECX,
            ModRmOffsetType.EDX => _state.EDX,
            ModRmOffsetType.EBX => _state.EBX,
            ModRmOffsetType.SIB => ComputeSibValue(),
            ModRmOffsetType.EBP => _state.EBP,
            ModRmOffsetType.ESI => _state.ESI,
            ModRmOffsetType.EDI => _state.EDI,
            _ => throw new ArgumentOutOfRangeException(nameof(ModRmContext.ModRmOffsetType), ModRmContext.ModRmOffsetType, "value not handled")
        };
    }

    private ushort GetOffsetFieldValue() {
        if (ModRmContext.ModRmOffsetField == null) {
            throw new ArgumentOutOfRangeException(nameof(ModRmContext.ModRmOffsetField), ModRmContext.ModRmOffsetField, "Should not be null");
        }
        return _instructionFieldValueRetriever.GetFieldValue(ModRmContext.ModRmOffsetField);
    }

    private uint ComputeSibValue() {
        if (ModRmContext.SibContext == null) {
            throw new ArgumentOutOfRangeException(nameof(ModRmContext.SibContext), ModRmContext.SibContext, "Sib value asked but SibContext is null");
        }
        SibContext sibContext = ModRmContext.SibContext;
        uint @base = ComputeSibBase(sibContext);
        uint index = ComputeSibIndex(sibContext);
        return (uint)(@base + sibContext.Scale * index);
    }

    private uint ComputeSibBase(SibContext sibContext) {
        return sibContext.SibBase switch {
            SibBase.EAX => _state.EAX,
            SibBase.ECX => _state.ECX,
            SibBase.EDX => _state.EDX,
            SibBase.EBX => _state.EBX,
            SibBase.ESP => _state.ESP,
            SibBase.BASE_FIELD_32 => GetSibBaseFieldValue(sibContext),
            SibBase.EBP => _state.EBP,
            SibBase.ESI => _state.ESI,
            SibBase.EDI => _state.EDI,
            _ => throw new ArgumentOutOfRangeException(nameof(sibContext.SibBase), sibContext.SibBase, "value not handled")
        };
    }
    private uint GetSibBaseFieldValue(SibContext sibContext) {
        if (sibContext.BaseField == null) {
            throw new ArgumentOutOfRangeException(nameof(sibContext.BaseField), sibContext.BaseField, "Should not be null");
        }
        return _instructionFieldValueRetriever.GetFieldValue(sibContext.BaseField);
    }

    private uint ComputeSibIndex(SibContext sibContext) {
        return sibContext.SibIndex switch {
            SibIndex.EAX => _state.EAX,
            SibIndex.ECX => _state.ECX,
            SibIndex.EDX => _state.EDX,
            SibIndex.EBX => _state.EBX,
            SibIndex.ZERO => 0,
            SibIndex.EBP => _state.EBP,
            SibIndex.ESI => _state.ESI,
            SibIndex.EDI => _state.EDI,
            _ => throw new ArgumentOutOfRangeException(nameof(sibContext.SibIndex), sibContext.SibIndex, "value not handled")
        };
    }

    private InvalidOperationException CreateInvalidOperationExceptionForNullFieldButModeNot3(string fieldName) {
        return new InvalidOperationException($"{fieldName} should not be null except when Mode==3. This is a bug in ModRm code. Mode value is {ModRmContext.Mode}");
    }
}