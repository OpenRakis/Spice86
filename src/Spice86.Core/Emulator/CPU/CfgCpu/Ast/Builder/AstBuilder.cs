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
    public AstBuilder() {
        InstructionField = new(Constant, Pointer);
        ModRm = new(Register, InstructionField, Pointer);
    }

    public RegisterAstBuilder Register { get; } = new();
    public PointerAstBuilder Pointer { get; } = new();
    public ConstantAstBuilder Constant { get; } = new();
    public InstructionFieldAstBuilder InstructionField { get; }
    public ModRmAstBuilder ModRm { get; }

    public DataType SType(int size) {
        return Type(size, true);
    }
    public DataType UType(int size) {
        return Type(size, false);
    }

    private DataType Type(int size, bool isSigned) {
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

}