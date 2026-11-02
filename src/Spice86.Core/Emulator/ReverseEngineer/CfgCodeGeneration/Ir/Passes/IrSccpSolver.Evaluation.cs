namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Linq;
using System.Numerics;

internal sealed partial class IrSccpSolver {
    /// <summary>The right side of the direction-flag write of <paramref name="node"/>: a
    /// <c>DirectionFlag = x</c> assignment, or a flag-register assignment (whose constant
    /// <see cref="Flags.Direction"/> bit decides the flag).</summary>
    private static ValueNode? DirectionWriter(IVisitableAstNode node) => node switch {
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
            Left: CpuFlagNode { FlagMask: Flags.Direction } } assignment => assignment.Right,
        BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
            Left: FlagRegisterNode } assignment => assignment.Right,
        _ => null
    };

    /// <summary>The direction flag <paramref name="node"/> writes, or false when it is not a
    /// flag write the facts evaluate to a constant.</summary>
    private bool TryEvaluateDirectionWriter(IVisitableAstNode node, IrBlock block, int index,
        out ulong direction) {
        direction = 0;
        switch (node) {
            case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: CpuFlagNode { FlagMask: Flags.Direction } } flagAssignment:
                if (!facts.TryEvaluate(flagAssignment.Right, block, index, out direction)) {
                    return false;
                }
                direction = direction != 0 ? 1UL : 0UL;
                return true;
            case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: FlagRegisterNode } registerAssignment:
                if (!facts.TryEvaluate(registerAssignment.Right, block, index, out ulong flags)) {
                    return false;
                }
                direction = (flags & Flags.Direction) != 0 ? 1UL : 0UL;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The single register assignment of <paramref name="node"/> writing exactly
    /// <paramref name="writtenMask"/> bytes of register <paramref name="registerIndex"/>, or null.</summary>
    private static BinaryOperationNode? WriterOf(IVisitableAstNode node, int registerIndex,
        uint writtenMask) =>
        node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN,
                Left: RegisterNode register } assignment
            && BitOperations.TrailingZeroCount((ulong)IrValueSets.Of(register))
                / ByteCount == registerIndex
            && (((ulong)IrValueSets.Of(register) >> (ByteCount * registerIndex)) & AllBytes)
                == writtenMask
            ? assignment
            : null;

    /// <summary>The per-byte meet of <paramref name="operandIds"/>: a byte is known when every
    /// determined operand knows it and all agree, undetermined while every operand is undetermined,
    /// varying otherwise.</summary>
    private (uint Known, uint Undetermined, uint Value) Meet(List<int> operandIds,
        bool hasEntryOperand) {
        uint meetKnown = 0;
        uint meetUndetermined = 0;
        uint meetValue = 0;
        int operandCount = operandIds.Count + (hasEntryOperand ? 1 : 0);
        for (int b = 0; b < ByteCount; b++) {
            uint bit = 1U << b;
            List<int> determined = [.. operandIds
                .Where(id => (facts.Undetermined[id] & bit) == 0)];
            bool undetermined = determined.Count != operandCount;
            bool known = determined.Count > 0;
            uint ByteOf(int id) => (facts.Value[id] >> (8 * b)) & 0xFF;
            uint byteValue = known ? ByteOf(determined[0]) : 0;
            bool varying = hasEntryOperand
                || determined.Any(id => (facts.Known[id] & bit) == 0)
                || determined.Any(id => ByteOf(id) != byteValue);
            if (varying) {
                continue;
            }
            if (known) {
                meetKnown |= bit;
                meetValue |= byteValue << (8 * b);
            } else if (undetermined) {
                meetUndetermined |= bit;
            }
        }
        return (meetKnown, meetUndetermined, meetValue);
    }
}
