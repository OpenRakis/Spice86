namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Facts an <c>int</c> instruction node carries: which vector it calls, and the value its
/// in-block predecessor loads into AH. Shared by the speculative explorer (handler seeding,
/// DOS-exit detection) and the callee write scanner (service contract dispatch).
/// </summary>
internal static class InterruptCallFacts {
    /// <summary>The interrupt vector of <paramref name="node"/>, when it is an <c>int</c>
    /// instruction whose vector number is a constant.</summary>
    public static bool TryGetInterruptVector(CfgInstruction node, out byte vector) {
        vector = 0;
        if (node.ExecutionAst is not InterruptCallNode call) {
            return false;
        }
        ulong? number = call.VectorNumber switch {
            ConstantNode constant => constant.Value,
            InstructionFieldNode field => field.ConstantValue,
            _ => null
        };
        if (number is null) {
            return false;
        }
        vector = (byte)number.Value;
        return true;
    }

    /// <summary>
    /// The value the single predecessor of <paramref name="node"/> inside its own block loads
    /// into AH, when that predecessor is a <c>mov ah, imm8</c> or <c>mov ax, imm16</c>;
    /// <see langword="null"/> otherwise (the byte the mov loads into AH: the immediate itself
    /// for mov ah,imm8, the high byte for mov ax,imm16).
    /// </summary>
    public static byte? AhLoadedBefore(CfgInstruction node) {
        CfgInstruction? previous = null;
        foreach (ICfgNode predecessor in node.Predecessors) {
            if (predecessor is CfgInstruction instruction && instruction.ContainingBlock == node.ContainingBlock) {
                if (previous is not null) {
                    return null;
                }
                previous = instruction;
            }
        }
        if (previous is null) {
            return null;
        }
        if (previous.ExecutionAst is not BlockNode block || block.Statements.Count == 0
            || block.Statements[0] is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode destination
            } assign) {
            return null;
        }
        ulong? immediate = assign.Right switch {
            ConstantNode constant => constant.Value,
            InstructionFieldNode field => field.ConstantValue,
            _ => null
        };
        if (immediate is null) {
            return null;
        }
        ulong? ah = destination switch {
            { DataType.BitWidth: BitWidth.BYTE_8, RegisterIndex: 4 } => immediate.Value,
            { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: 0 } => immediate.Value >> 8,
            _ => null
        };
        return ah is null ? null : (byte)ah.Value;
    }
}
