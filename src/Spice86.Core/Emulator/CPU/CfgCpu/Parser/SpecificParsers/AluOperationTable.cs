namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Shared ALU operation table used by both AluOperationParser (opcodes 0x00-0x3D)
/// and Grp1Parser (opcodes 0x80-0x83). Indexed by the 3-bit operation field
/// (bits 5-3 of opcode for ALU block, ModRM.reg for GRP1).
/// </summary>
public static class AluOperationTable {
    /// <summary>
    /// ALU operations: (method name, display operation, whether to assign result back).
    /// CMP uses "Sub" as its method but does not assign the result.
    /// </summary>
    public static readonly (string Operation, InstructionOperation DisplayOp, bool Assign)[] Operations = {
        ("Add", InstructionOperation.ADD, true),
        ("Or", InstructionOperation.OR, true),
        ("Adc", InstructionOperation.ADC, true),
        ("Sbb", InstructionOperation.SBB, true),
        ("And", InstructionOperation.AND, true),
        ("Sub", InstructionOperation.SUB, true),
        ("Xor", InstructionOperation.XOR, true),
        ("Sub", InstructionOperation.CMP, false),
    };
}
