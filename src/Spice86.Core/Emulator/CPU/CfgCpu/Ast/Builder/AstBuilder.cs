namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class AstBuilder {
    public DataType SType(int size) {
        return Type(size, true);
    }

    public DataType UType(int size) {
        return Type(size, false);
    }

    public DataType Type(int size, bool isSigned) {
        return size switch {
            8 => isSigned ? DataType.INT8 : DataType.UINT8,
            16 => isSigned ? DataType.INT16 : DataType.UINT16,
            32 => isSigned ? DataType.INT32 : DataType.UINT32,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "value not handled")
        };
    }

    public DataType AddressType(CfgInstruction instruction) {
        return instruction.AddressSize32Prefix == null ? DataType.UINT16 : DataType.UINT32;
    }

    public RepPrefix? Rep(StringInstruction instruction) {
        if (instruction.RepPrefix is null) {
            return null;
        }
        if (!instruction.ChangesFlags) {
            return RepPrefix.REP;
        }
        if (instruction.RepPrefix.ContinueZeroFlagValue) {
            return RepPrefix.REPE;
        }
        return RepPrefix.REPNE;
    }

    public ValueNode ToSegmentedPointer(DataType targetDataType, SegmentRegisterIndex segmentRegisterIndex, ValueNode offset) {
        return ToSegmentedPointer(targetDataType, (int)segmentRegisterIndex, offset);
    }

    public ValueNode ToSegmentedPointer(DataType targetDataType, int segmentRegisterIndex, ValueNode offset) {
        return ToSegmentedPointer(targetDataType, new SegmentRegisterNode(segmentRegisterIndex), offset);
    }

    public ValueNode ToSegmentedPointer(DataType targetDataType, ValueNode segment, ValueNode offset) {
        return new SegmentedPointer(targetDataType, segment, offset);
    }

    public ValueNode? ToNode(InstructionField<byte> field, bool nullIfZero = false) {
        return ToNode(ToType(field), field.Value, field.UseValue, field.PhysicalAddress, nullIfZero);
    }

    public ValueNode? ToNode(InstructionField<ushort> field, bool nullIfZero = false) {
        return ToNode(ToType(field), field.Value, field.UseValue, field.PhysicalAddress, nullIfZero);
    }

    public ValueNode? ToNode(InstructionField<uint> field, bool nullIfZero = false) {
        return ToNode(ToType(field), field.Value, field.UseValue, field.PhysicalAddress, nullIfZero);
    }

    public ValueNode? ToNode(InstructionField<sbyte> field, bool nullIfZero = false) {
        return ToNode(ToType(field), (uint)field.Value, field.UseValue, field.PhysicalAddress, nullIfZero);
    }

    public ValueNode? ToNode(InstructionField<short> field, bool nullIfZero = false) {
        return ToNode(ToType(field), (uint)field.Value, field.UseValue, field.PhysicalAddress, nullIfZero);
    }

    public ValueNode? ToNode(InstructionField<int> field, bool nullIfZero = false) {
        return ToNode(ToType(field), (uint)field.Value, field.UseValue, field.PhysicalAddress, nullIfZero);
    }
    
    public DataType ToType(InstructionField<byte> field) {
        return DataType.UINT8;
    }
    
    public DataType ToType(InstructionField<ushort> field) {
        return DataType.UINT16;
    }
    
    public DataType ToType(InstructionField<uint> field) {
        return DataType.UINT32;
    }
    
    public DataType ToType(InstructionField<sbyte> field) {
        return DataType.INT8;
    }
    
    public DataType ToType(InstructionField<short> field) {
        return DataType.INT16;
    }

    public DataType ToType(InstructionField<int> field) {
        return DataType.INT32;
    }

    public ValueNode ToNode(byte value) {
        return new ConstantNode(DataType.UINT8, value);
    }

    public ValueNode ToNode(ushort value) {
        return new ConstantNode(DataType.UINT16, value);
    }

    public ValueNode ToNode(uint value) {
        return new ConstantNode(DataType.UINT32, value);
    }

    public ValueNode ToNode(sbyte value) {
        return new ConstantNode(DataType.INT8, (byte)value);
    }

    public ValueNode ToNode(short value) {
        return new ConstantNode(DataType.INT16, (ushort)value);
    }

    public ValueNode ToNode(int value) {
        return new ConstantNode(DataType.INT32, (uint)value);
    }

    public ValueNode ToNode(SegmentedAddress segmentedAddress) {
        return new SegmentedAddressConstantNode(segmentedAddress);
    }
    public ValueNode? ToNode(DataType type, uint value, bool useValue, uint physicalAddress, bool nullIfZero) {
        if (useValue) {
            if (value == 0 && nullIfZero) {
                return null;
            }
            return new ConstantNode(type, value);
        }
        return ToAbsolutePointer(type, physicalAddress);
    }

    public ValueNode ToNode(InstructionField<SegmentedAddress> field) {
        if (field.UseValue) {
            return ToNode(field.Value);
        }

        return ToAbsolutePointer(DataType.UINT32, field.PhysicalAddress);
    }

    public AbsolutePointerNode ToAbsolutePointer(DataType targetDataType, uint address) {
        return new AbsolutePointerNode(targetDataType, new ConstantNode(DataType.UINT32, address));
    }

    public ValueNode Reg8(RegisterIndex registerIndex) {
        return Reg(DataType.UINT8, registerIndex);
    }

    public ValueNode Reg16(RegisterIndex registerIndex) {
        return Reg(DataType.UINT16, registerIndex);
    }
    
    public ValueNode SReg(SegmentRegisterIndex registerIndex) {
        return SReg((int)registerIndex);
    }
    
    public ValueNode SReg(int segmentRegisterIndex) {
        return new SegmentRegisterNode(segmentRegisterIndex);
    }

    public ValueNode Reg32(RegisterIndex registerIndex) {
        return Reg(DataType.UINT32, registerIndex);
    }

    public ValueNode Accumulator(DataType dataType) {
        return new RegisterNode(dataType, (int)RegisterIndex.AxIndex);
    }

    public ValueNode Reg(DataType dataType, RegisterIndex registerIndex) {
        return Reg(dataType, (int)registerIndex);
    }

    public ValueNode Reg(DataType dataType, int registerIndex) {
        return new RegisterNode(dataType, registerIndex);
    }

    public ValueNode RmToNode(DataType targetDataType, ModRmContext modRmContext) {
        if (modRmContext.MemoryAddressType == MemoryAddressType.NONE) {
            // then it's a register
            return RToNode(targetDataType, modRmContext);
        }

        return ToMemoryAddressNode(targetDataType, modRmContext);
    }

    public ValueNode RToNode(DataType dataType, ModRmContext modRmContext) {
        return new RegisterNode(dataType, modRmContext.RegisterMemoryIndex);
    }

    public ValueNode ToMemoryAddressNode(DataType targetDataType, ModRmContext modRmContext) {
        if (modRmContext.MemoryAddressType == MemoryAddressType.NONE) {
            throw new ArgumentException(
                $"MemoryAddressType is {modRmContext.MemoryAddressType} which should never happen when computing addresses.");
        }

        if (modRmContext.SegmentIndex == null) {
            throw new ArgumentException("SegmentIndex is null");
        }

        ValueNode segment = new SegmentRegisterNode(modRmContext.SegmentIndex.Value);
        ValueNode offset = ModRmMemoryOffsetToNode(modRmContext);
        return ToSegmentedPointer(targetDataType, segment, offset);
    }

    public ValueNode ModRmMemoryOffsetToNode(ModRmContext modRmContext) {
        if (modRmContext.MemoryOffsetType == MemoryOffsetType.NONE) {
            throw new ArgumentException(
                $"MemoryOffsetType is {modRmContext.MemoryOffsetType} which should never happen when computing offsets.");
        }

        ValueNode? displacement = ModRmDisplacementToNode(modRmContext);
        ValueNode? offset = ModRmOffsetToNode(modRmContext);
        ValueNode? result = BiOperationWithResultNode(new DataType(modRmContext.AddressSize, false), offset, Operation.PLUS,
            displacement);
        return result ?? new ConstantNode(new DataType(modRmContext.AddressSize, false), 0);
    }

    public ValueNode? ModRmDisplacementToNode(ModRmContext modRmContext) {
        if (modRmContext.DisplacementType == DisplacementType.ZERO) {
            return null;
        }

        return modRmContext.DisplacementType switch {
            DisplacementType.INT8 => ToNode((InstructionField<sbyte>)EnsureNonNull(modRmContext.DisplacementField)),
            DisplacementType.INT16 => ToNode((InstructionField<short>)EnsureNonNull(modRmContext.DisplacementField)),
            DisplacementType.INT32 => ToNode((InstructionField<int>)EnsureNonNull(modRmContext.DisplacementField)),
            _ => throw new ArgumentOutOfRangeException(nameof(modRmContext.DisplacementType),
                modRmContext.DisplacementType, "value not handled")
        };
    }

    public ValueNode? ModRmOffsetToNode(ModRmContext modRmContext) {
        return modRmContext.ModRmOffsetType switch {
            ModRmOffsetType.BX_PLUS_SI => PlusRegs16(RegisterIndex.BxIndex, RegisterIndex.SiIndex),
            ModRmOffsetType.BX_PLUS_DI => PlusRegs16(RegisterIndex.BxIndex, RegisterIndex.DiIndex),
            ModRmOffsetType.BP_PLUS_SI => PlusRegs16(RegisterIndex.BpIndex, RegisterIndex.SiIndex),
            ModRmOffsetType.BP_PLUS_DI => PlusRegs16(RegisterIndex.BpIndex, RegisterIndex.DiIndex),
            ModRmOffsetType.SI => Reg16(RegisterIndex.SiIndex),
            ModRmOffsetType.DI => Reg16(RegisterIndex.DiIndex),
            ModRmOffsetType.OFFSET_FIELD_16 => ToNode(EnsureNonNull(modRmContext.ModRmOffsetField), true),
            ModRmOffsetType.BP => Reg16(RegisterIndex.BpIndex),
            ModRmOffsetType.BX => Reg16(RegisterIndex.BxIndex),
            ModRmOffsetType.EAX => Reg32(RegisterIndex.AxIndex),
            ModRmOffsetType.ECX => Reg32(RegisterIndex.CxIndex),
            ModRmOffsetType.EDX => Reg32(RegisterIndex.DxIndex),
            ModRmOffsetType.EBX => Reg32(RegisterIndex.BxIndex),
            ModRmOffsetType.SIB => SibValueToNode(EnsureNonNull(modRmContext.SibContext)),
            ModRmOffsetType.EBP => Reg32(RegisterIndex.BpIndex),
            ModRmOffsetType.ESI => Reg32(RegisterIndex.SiIndex),
            ModRmOffsetType.EDI => Reg32(RegisterIndex.DiIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(modRmContext.ModRmOffsetType),
                modRmContext.ModRmOffsetType, "value not handled")
        };
    }

    private ValueNode? SibValueToNode(SibContext sibContext) {
        ValueNode? baseNode = SibBaseToNode(sibContext);
        ValueNode? indexNode = SibIndexToNode(sibContext);
        // base + scale * index
        ValueNode scaleNode = new ConstantNode(DataType.UINT8, sibContext.Scale);
        ValueNode? indexExpression =
            BiOperationWithResultNode(DataType.UINT32, scaleNode, Operation.MULTIPLY, indexNode);
        return BiOperationWithResultNode(DataType.UINT32, baseNode, Operation.PLUS, indexExpression);
    }

    private ValueNode? SibBaseToNode(SibContext sibContext) {
        return sibContext.SibBase switch {
            SibBase.EAX => Reg32(RegisterIndex.AxIndex),
            SibBase.ECX => Reg32(RegisterIndex.CxIndex),
            SibBase.EDX => Reg32(RegisterIndex.DxIndex),
            SibBase.EBX => Reg32(RegisterIndex.BxIndex),
            SibBase.ESP => Reg32(RegisterIndex.SpIndex),
            SibBase.BASE_FIELD_32 => ToNode(EnsureNonNull(sibContext.BaseField), true),
            SibBase.EBP => Reg32(RegisterIndex.BpIndex),
            SibBase.ESI => Reg32(RegisterIndex.SiIndex),
            SibBase.EDI => Reg32(RegisterIndex.DiIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(sibContext.SibBase), sibContext.SibBase,
                "value not handled")
        };
    }

    private ValueNode? SibIndexToNode(SibContext sibContext) {
        return sibContext.SibIndex switch {
            SibIndex.EAX => Reg32(RegisterIndex.AxIndex),
            SibIndex.ECX => Reg32(RegisterIndex.CxIndex),
            SibIndex.EDX => Reg32(RegisterIndex.DxIndex),
            SibIndex.EBX => Reg32(RegisterIndex.BxIndex),
            SibIndex.ZERO => null,
            SibIndex.EBP => Reg32(RegisterIndex.BpIndex),
            SibIndex.ESI => Reg32(RegisterIndex.SiIndex),
            SibIndex.EDI => Reg32(RegisterIndex.DiIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(sibContext.SibIndex), sibContext.SibIndex,
                "value not handled")
        };
    }

    private ValueNode PlusRegs16(RegisterIndex registerIndex1, RegisterIndex registerIndex2) {
        return new BinaryOperationNode(DataType.UINT16, Reg16(registerIndex1), Operation.PLUS,
            Reg16(registerIndex2));
    }

    private ValueNode? BiOperationWithResultNode(DataType dataType,
        ValueNode? parameter1,
        Operation operation,
        ValueNode? parameter2) {
        if (parameter1 != null && parameter2 != null) {
            return new BinaryOperationNode(dataType, parameter1, operation, parameter2);
        }

        if (parameter1 == null) {
            return parameter2;
        }

        return parameter1;
    }

    private T EnsureNonNull<T>(T? argument) {
        ArgumentNullException.ThrowIfNull(argument);
        return argument;
    }
}