namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Shared ALU operation table used by both AluOperationParser (opcodes 0x00-0x3D)
/// and Grp1Parser (opcodes 0x80-0x83). Indexed by the 3-bit operation field
/// (bits 5-3 of opcode for ALU block, ModRM.reg for GRP1).
/// </summary>
public static class AluOperationTable {
    /// <summary>
    /// ALU operations: (operation, display operation, whether to assign result back).
    /// CMP uses Sub but does not assign the result.
    /// </summary>
    public static readonly (AluOperation Operation, InstructionOperation DisplayOp, bool Assign)[] Operations = {
        (AluOperation.Add, InstructionOperation.ADD, true),
        (AluOperation.Or, InstructionOperation.OR, true),
        (AluOperation.Adc, InstructionOperation.ADC, true),
        (AluOperation.Sbb, InstructionOperation.SBB, true),
        (AluOperation.And, InstructionOperation.AND, true),
        (AluOperation.Sub, InstructionOperation.SUB, true),
        (AluOperation.Xor, InstructionOperation.XOR, true),
        (AluOperation.Sub, InstructionOperation.CMP, false),
    };
}
