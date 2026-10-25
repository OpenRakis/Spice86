namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using System.Linq;

/// <summary>
/// Removes the plain flag writes of an instruction whose flags are dead at that point: a <c>clc</c> /
/// <c>stc</c> / <c>cmc</c> style <c>Flag = value</c> statement goes away when that flag is not read
/// later, and a 32-bit <c>FlagRegister = value</c> write (the <c>sahf</c> low-byte store) goes away
/// when no status flag is read later. A write whose value reads memory, a call, or a variable stays,
/// because removing it would drop a side effect or strand the variable's own assignment.
/// </summary>
internal static class DeadFlagAssignmentRemoval {
    /// <summary>
    /// <paramref name="instruction"/> without its dead plain flag writes, or the node itself when it is
    /// not a <see cref="BlockNode"/> or nothing is removed.
    /// </summary>
    /// <param name="instruction">The execution AST of the instruction.</param>
    /// <param name="flagsLiveAfter">The status flags live after the instruction.</param>
    public static IVisitableAstNode Remove(IVisitableAstNode instruction, StatusFlags flagsLiveAfter) {
        if (instruction is not BlockNode block) {
            return instruction;
        }
        StatusFlags live = flagsLiveAfter;
        List<IVisitableAstNode>? kept = null;
        for (int i = block.Statements.Count - 1; i >= 0; i--) {
            IVisitableAstNode statement = block.Statements[i];
            if (IsDeadFlagWrite(statement, live)) {
                kept ??= [.. block.Statements];
                kept.RemoveAt(i);
                continue;
            }
            FlagEffect effect = FlagEffectVisitor.Of(statement);
            live = (live & ~effect.MustWrite) | effect.Reads;
        }
        return kept is null ? instruction : new BlockNode(kept.ToArray());
    }

    /// <summary>
    /// Whether <paramref name="statement"/> is a flag write whose value is plain and whose flags are not
    /// in <paramref name="live"/>: a single status flag assignment, or a write to the 32-bit flag
    /// register (the form that only changes the status byte) when no status flag is live.
    /// </summary>
    private static bool IsDeadFlagWrite(IVisitableAstNode statement, StatusFlags live) =>
        statement is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment
        && IsPlain(assignment.Right)
        && assignment.Left switch {
            CpuFlagNode flag => IsStatusFlag(flag.FlagMask)
                && ((StatusFlags)flag.FlagMask & live) == StatusFlags.None,
            FlagRegisterNode flagRegister when flagRegister.DataType == DataType.UINT32 =>
                live == StatusFlags.None,
            _ => false
        };

    private static bool IsStatusFlag(ushort flagMask) =>
        flagMask != 0 && (flagMask & ~(ushort)StatusFlags.All) == 0;

    /// <summary>
    /// Whether <paramref name="node"/> is built only from constants, registers, flags, conversions, and
    /// unary / non-assignment binary operations: no memory read, no call, no variable reference.
    /// </summary>
    private static bool IsPlain(ValueNode node) => node switch {
        ConstantNode or RegisterNode or CpuFlagNode or FlagRegisterNode => true,
        TypeConversionNode conversion => IsPlain(conversion.Value),
        UnaryOperationNode unary => IsPlain(unary.Value),
        BinaryOperationNode { BinaryOperation: not BinaryOperation.ASSIGN } binary =>
            IsPlain(binary.Left) && IsPlain(binary.Right),
        _ => false
    };
}
