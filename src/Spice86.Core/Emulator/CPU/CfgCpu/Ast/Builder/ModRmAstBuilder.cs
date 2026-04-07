namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Registers;

public class ModRmAstBuilder(RegisterAstBuilder register, InstructionFieldAstBuilder instructionField, PointerAstBuilder pointer, ConstantAstBuilder constant) {
    private readonly TypeConversionAstBuilder _typeConversion = new();

    public RegisterAstBuilder Register { get; } = register;
    public InstructionFieldAstBuilder InstructionField { get; } = instructionField;
    public PointerAstBuilder Pointer { get; } = pointer;
    public ConstantAstBuilder Constant { get; } = constant;

    public ValueNode RmToNode(DataType targetDataType, ModRmContext modRmContext) {
        if (modRmContext.MemoryAddressType == MemoryAddressType.NONE) {
            // then it's a register
            return new RegisterNode(targetDataType, modRmContext.RegisterMemoryIndex);
        }

        return ToMemoryAddressNode(targetDataType, modRmContext);
    }

    public ValueNode RmToNodeSigned(DataType targetDataType, ModRmContext modRmContext) {
        return _typeConversion.ToSigned(RmToNode(targetDataType, modRmContext));
    }

    public ValueNode RToNode(DataType dataType, ModRmContext modRmContext) {
        return new RegisterNode(dataType, modRmContext.RegisterIndex);
    }

    public ValueNode RToNodeSigned(DataType dataType, ModRmContext modRmContext) {
        return _typeConversion.ToSigned(RToNode(dataType, modRmContext));
    }

    public ValueNode ToMemoryAddressNode(DataType targetDataType, ModRmContext modRmContext) {
        ValueNode offset = MemoryOffsetToNode(modRmContext);
        return ToSegmentedPointerFromContext(targetDataType, modRmContext, offset);
    }

    /// <summary>
    /// Caches the ModRM memory offset in a local variable, then builds a segmented pointer using that cached offset.
    /// Returns both the variable declaration (to include in the block) and the resulting pointer node.
    /// </summary>
    public (VariableDeclarationNode CachedOffset, ValueNode Pointer) ToMemoryAddressNodeWithCachedOffset(
        DataType targetDataType, DataType addressType, ModRmContext modRmContext, string variableName) {
        ValueNode offsetExpr = MemoryOffsetToNode(modRmContext);
        VariableDeclarationNode cachedOffset = new(addressType, variableName, offsetExpr);
        ValueNode pointer = ToSegmentedPointerFromContext(targetDataType, modRmContext, cachedOffset.Reference);
        return (cachedOffset, pointer);
    }

    /// <summary>
    /// Builds a segmented pointer using the ModRM context's segment registers with a caller-provided offset.
    /// </summary>
    public ValueNode ToMemoryAddressNodeWithCustomOffset(DataType targetDataType, ModRmContext modRmContext, ValueNode offset) {
        return ToSegmentedPointerFromContext(targetDataType, modRmContext, offset);
    }

    private ValueNode ToSegmentedPointerFromContext(DataType targetDataType, ModRmContext modRmContext, ValueNode offset) {
        if (modRmContext.MemoryAddressType == MemoryAddressType.NONE) {
            throw new ArgumentException(
                $"MemoryAddressType is {modRmContext.MemoryAddressType} which should never happen when computing addresses.");
        }

        if (modRmContext.SegmentIndex == null || modRmContext.DefaultSegmentIndex == null) {
            throw new ArgumentException("SegmentIndex is null");
        }

        ValueNode defaultSegment = new SegmentRegisterNode(modRmContext.DefaultSegmentIndex.Value);
        ValueNode segment = new SegmentRegisterNode(modRmContext.SegmentIndex.Value);
        return Pointer.ToSegmentedPointer(targetDataType, segment, defaultSegment, offset);
    }

    public ValueNode MemoryOffsetToNode(ModRmContext modRmContext) {
        if (modRmContext.MemoryOffsetType == MemoryOffsetType.NONE) {
            throw new ArgumentException(
                $"MemoryOffsetType is {modRmContext.MemoryOffsetType} which should never happen when computing offsets.");
        }

        DataType addressType = new DataType(modRmContext.AddressSize, false);
        ValueNode? displacement = ModRmDisplacementToNode(modRmContext);
        ValueNode? offset = ModRmOffsetToNode(modRmContext);
        ValueNode? result = BiOperationWithResultNode(addressType, offset, BinaryOperation.PLUS,
            displacement);
        return result ?? new ConstantNode(addressType, 0);
    }

    /// <summary>
    /// Creates a memory address node with an adjusted offset.
    /// Useful for instructions that need to access memory at an offset from the ModRM address.
    /// </summary>
    public ValueNode ToMemoryAddressNodeWithOffsetAdjustment(DataType targetDataType, ModRmContext modRmContext, ValueNode offsetAdjustment) {
        ValueNode basePointer = ToMemoryAddressNode(targetDataType, modRmContext);
        return Pointer.WithOffsetAdjustment((SegmentedPointerNode)basePointer, offsetAdjustment);
    }

    /// <summary>
    /// Reads a segmented address (offset then segment) from the memory location pointed to by ModRM.
    /// </summary>
    public SegmentedAddressValueNode ToSegmentedAddressNode(int operandSize, ModRmContext modRmContext) {
        DataType offsetType = operandSize == 16 ? DataType.UINT16 : DataType.UINT32;
        ValueNode ipNode = ToMemoryAddressNode(offsetType, modRmContext);
        if (operandSize == 32) {
            ipNode = _typeConversion.Convert(DataType.UINT16, ipNode);
        }
        ValueNode csNode = ToMemoryAddressNodeWithOffsetAdjustment(
            DataType.UINT16, modRmContext, Constant.ToNode((ushort)(operandSize / 8)));
        return new SegmentedAddressValueNode(csNode, ipNode);
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
            ModRmOffsetType.ZERO => new ConstantNode(new DataType(modRmContext.AddressSize, false), 0),
            _ => throw new ArgumentOutOfRangeException(nameof(modRmContext.ModRmOffsetType),
                modRmContext.ModRmOffsetType, "value not handled")
        };
    }

    private ValueNode? SibValueToNode(SibContext sibContext) {
        ValueNode? baseNode = SibBaseToNode(sibContext);
        ValueNode? indexNode = SibIndexToNode(sibContext);
        // base + scale * index (when index is null/zero, skip the scale*index term entirely)
        ValueNode? indexExpression = null;
        if (indexNode != null) {
            ValueNode scaleNode = new ConstantNode(DataType.UINT32, sibContext.Scale);
            indexExpression = BiOperationWithResultNode(DataType.UINT32, scaleNode, BinaryOperation.MULTIPLY, indexNode);
        }
        return BiOperationWithResultNode(DataType.UINT32, baseNode, BinaryOperation.PLUS, indexExpression);
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
        return new BinaryOperationNode(DataType.UINT16, Register.Reg16(registerIndex1), BinaryOperation.PLUS,
            Register.Reg16(registerIndex2));
    }

    private ValueNode? BiOperationWithResultNode(DataType dataType,
        ValueNode? parameter1,
        BinaryOperation binaryOperation,
        ValueNode? parameter2) {
        if (parameter1 != null && parameter2 != null) {
            return new BinaryOperationNode(dataType, parameter1, binaryOperation, parameter2);
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