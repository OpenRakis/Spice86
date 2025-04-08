namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Registers;

public class ModRmAstBuilder(RegisterAstBuilder register, InstructionFieldAstBuilder instructionField, PointerAstBuilder pointer) {
    public RegisterAstBuilder Register { get; } = register;
    public InstructionFieldAstBuilder InstructionField { get; } = instructionField;
    public PointerAstBuilder Pointer { get; } = pointer;

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
        ValueNode offset = MemoryOffsetToNode(modRmContext);
        return Pointer.ToSegmentedPointer(targetDataType, segment, offset);
    }

    public ValueNode MemoryOffsetToNode(ModRmContext modRmContext) {
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

    private ValueNode? ModRmDisplacementToNode(ModRmContext modRmContext) {
        if (modRmContext.DisplacementType == DisplacementType.ZERO) {
            return null;
        }

        return modRmContext.DisplacementType switch {
            DisplacementType.INT8 => InstructionField.ToNode((InstructionField<sbyte>)EnsureNonNull(modRmContext.DisplacementField)),
            DisplacementType.INT16 => InstructionField.ToNode((InstructionField<short>)EnsureNonNull(modRmContext.DisplacementField)),
            DisplacementType.INT32 => InstructionField.ToNode((InstructionField<int>)EnsureNonNull(modRmContext.DisplacementField)),
            _ => throw new ArgumentOutOfRangeException(nameof(modRmContext.DisplacementType),
                modRmContext.DisplacementType, "value not handled")
        };
    }

    private ValueNode? ModRmOffsetToNode(ModRmContext modRmContext) {
        return modRmContext.ModRmOffsetType switch {
            ModRmOffsetType.BX_PLUS_SI => PlusRegs16(RegisterIndex.BxIndex, RegisterIndex.SiIndex),
            ModRmOffsetType.BX_PLUS_DI => PlusRegs16(RegisterIndex.BxIndex, RegisterIndex.DiIndex),
            ModRmOffsetType.BP_PLUS_SI => PlusRegs16(RegisterIndex.BpIndex, RegisterIndex.SiIndex),
            ModRmOffsetType.BP_PLUS_DI => PlusRegs16(RegisterIndex.BpIndex, RegisterIndex.DiIndex),
            ModRmOffsetType.SI => Register.Reg16(RegisterIndex.SiIndex),
            ModRmOffsetType.DI => Register.Reg16(RegisterIndex.DiIndex),
            ModRmOffsetType.OFFSET_FIELD_16 => InstructionField.ToNode(EnsureNonNull(modRmContext.ModRmOffsetField), true),
            ModRmOffsetType.BP => Register.Reg16(RegisterIndex.BpIndex),
            ModRmOffsetType.BX => Register.Reg16(RegisterIndex.BxIndex),
            ModRmOffsetType.EAX => Register.Reg32(RegisterIndex.AxIndex),
            ModRmOffsetType.ECX => Register.Reg32(RegisterIndex.CxIndex),
            ModRmOffsetType.EDX => Register.Reg32(RegisterIndex.DxIndex),
            ModRmOffsetType.EBX => Register.Reg32(RegisterIndex.BxIndex),
            ModRmOffsetType.SIB => SibValueToNode(EnsureNonNull(modRmContext.SibContext)),
            ModRmOffsetType.EBP => Register.Reg32(RegisterIndex.BpIndex),
            ModRmOffsetType.ESI => Register.Reg32(RegisterIndex.SiIndex),
            ModRmOffsetType.EDI => Register.Reg32(RegisterIndex.DiIndex),
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
            SibBase.EAX => Register.Reg32(RegisterIndex.AxIndex),
            SibBase.ECX => Register.Reg32(RegisterIndex.CxIndex),
            SibBase.EDX => Register.Reg32(RegisterIndex.DxIndex),
            SibBase.EBX => Register.Reg32(RegisterIndex.BxIndex),
            SibBase.ESP => Register.Reg32(RegisterIndex.SpIndex),
            SibBase.BASE_FIELD_32 => InstructionField.ToNode(EnsureNonNull(sibContext.BaseField), true),
            SibBase.EBP => Register.Reg32(RegisterIndex.BpIndex),
            SibBase.ESI => Register.Reg32(RegisterIndex.SiIndex),
            SibBase.EDI => Register.Reg32(RegisterIndex.DiIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(sibContext.SibBase), sibContext.SibBase,
                "value not handled")
        };
    }

    private ValueNode? SibIndexToNode(SibContext sibContext) {
        return sibContext.SibIndex switch {
            SibIndex.EAX => Register.Reg32(RegisterIndex.AxIndex),
            SibIndex.ECX => Register.Reg32(RegisterIndex.CxIndex),
            SibIndex.EDX => Register.Reg32(RegisterIndex.DxIndex),
            SibIndex.EBX => Register.Reg32(RegisterIndex.BxIndex),
            SibIndex.ZERO => null,
            SibIndex.EBP => Register.Reg32(RegisterIndex.BpIndex),
            SibIndex.ESI => Register.Reg32(RegisterIndex.SiIndex),
            SibIndex.EDI => Register.Reg32(RegisterIndex.DiIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(sibContext.SibIndex), sibContext.SibIndex,
                "value not handled")
        };
    }

    private ValueNode PlusRegs16(RegisterIndex registerIndex1, RegisterIndex registerIndex2) {
        return new BinaryOperationNode(DataType.UINT16, Register.Reg16(registerIndex1), Operation.PLUS,
            Register.Reg16(registerIndex2));
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