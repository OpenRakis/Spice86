namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Facts an <c>int</c> instruction node carries: which vector it calls, and the AH, AL or AX
/// value the code right before it loads. Shared by the speculative explorer (handler seeding,
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
        if (SingleBlockPredecessor(node) is not { } previous
            || FirstAssignment(previous) is not { Left: RegisterNode destination } assign
            || Immediate(assign.Right) is not { } immediate) {
            return null;
        }
        ulong? ah = destination switch {
            { DataType.BitWidth: BitWidth.BYTE_8, RegisterIndex: AhByteIndex } => immediate,
            { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: AxIndex } => immediate >> 8,
            _ => null
        };
        return ah is null ? null : (byte)ah.Value;
    }

    /// <summary>
    /// The constant AL and AH the straight-line run of in-block predecessors leaves right before
    /// <paramref name="node"/>, each null when the run does not set it. <c>mov ax,imm16</c> and
    /// <c>xor ax,ax</c> set both bytes, <c>mov al,imm8</c> and <c>mov ah,imm8</c> set one; any
    /// other instruction that may write AX clears both.
    /// </summary>
    public static (byte? Al, byte? Ah) AxBytesLoadedBefore(CfgInstruction node) {
        List<CfgInstruction> run = [];
        CfgInstruction? cursor = SingleBlockPredecessor(node);
        while (cursor is not null && run.Count < MaxStraightLineRun) {
            run.Add(cursor);
            cursor = SingleBlockPredecessor(cursor);
        }
        byte? al = null;
        byte? ah = null;
        for (int i = run.Count - 1; i >= 0; i--) {
            ApplyAxWrite(run[i], ref al, ref ah);
        }
        return (al, ah);
    }

    private const int AxIndex = 0;
    private const int AhByteIndex = 4;
    private const int MaxStraightLineRun = 16;

    private static void ApplyAxWrite(CfgInstruction instruction, ref byte? al, ref byte? ah) {
        if (FirstAssignment(instruction) is { Left: RegisterNode destination } assign) {
            ulong? immediate = Immediate(assign.Right);
            switch (destination) {
                case { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: AxIndex } when immediate is { } word:
                    al = (byte)word;
                    ah = (byte)(word >> 8);
                    return;
                case { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: AxIndex } when IsXorAxAx(assign.Right):
                    al = 0;
                    ah = 0;
                    return;
                case { DataType.BitWidth: BitWidth.BYTE_8, RegisterIndex: AxIndex } when immediate is { } low:
                    al = (byte)low;
                    return;
                case { DataType.BitWidth: BitWidth.BYTE_8, RegisterIndex: AhByteIndex } when immediate is { } high:
                    ah = (byte)high;
                    return;
            }
        }
        if ((RegisterWriteSet.MayWrite(instruction) & (ushort)RegisterBits.Ax) != 0) {
            al = null;
            ah = null;
        }
    }

    private static bool IsXorAxAx(IVisitableAstNode value) => value is AluCallValueNode {
        Operation: AluOperation.Xor,
        CallNode.Arguments: [
            RegisterNode { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: AxIndex },
            RegisterNode { DataType.BitWidth: BitWidth.WORD_16, RegisterIndex: AxIndex }
        ]
    };

    /// <summary>The only predecessor of <paramref name="node"/> inside its own block, or null when
    /// there is none or more than one.</summary>
    private static CfgInstruction? SingleBlockPredecessor(CfgInstruction node) {
        CfgInstruction? previous = null;
        foreach (ICfgNode predecessor in node.Predecessors) {
            if (predecessor is CfgInstruction instruction && instruction.ContainingBlock == node.ContainingBlock) {
                if (previous is not null) {
                    return null;
                }
                previous = instruction;
            }
        }
        return previous;
    }

    private static BinaryOperationNode? FirstAssignment(CfgInstruction instruction) =>
        instruction.ExecutionAst is BlockNode { Statements: [BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assign, ..] }
            ? assign
            : null;

    private static ulong? Immediate(IVisitableAstNode value) => value switch {
        ConstantNode constant => constant.Value,
        InstructionFieldNode field => field.ConstantValue,
        _ => null
    };
}
