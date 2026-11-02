namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// In an instruction that cannot fault and has no
/// after-check, a plain-value write to a status flag whose freshly defined version has no live use is
/// removed, and so is a write to the 32-bit flag register when no status flag it defines is live.
/// A write whose value reads memory, a call, or a variable must stay, because removing it would drop
/// a side effect or strand the variable's own assignment.
/// </summary>
internal static class IrDeadFlagWrites {
    /// <summary>Removes the dead plain flag writes of every rewritable instruction.</summary>
    public static void Apply(IrMethod method, IrSsa ssa) {
        List<(IrBlock Block, int Index)> removals = [];
        foreach ((ICfgNode origin, List<IrOrigins.Located> statements) in IrOrigins.ByOrigin(method)) {
            if (!IrOrigins.Rewritable(statements, origin)) {
                continue;
            }
            foreach (IrOrigins.Located located in statements.Where(
                    located => IsDeadFlagWrite(ssa, located.Statement))) {
                removals.Add((located.Block, located.Index));
            }
        }
        foreach ((IrBlock block, int index) in removals.OrderByDescending(removal => removal.Index)) {
            ssa.Remove(block.Statements[index]);
            block.Statements.RemoveAt(index);
        }
    }

    /// <summary>
    /// A plain assignment to a status flag all of whose mask flags are defined here and dead, or to the
    /// 32-bit flag register when every status flag the statement defines is dead.
    /// </summary>
    private static bool IsDeadFlagWrite(IrSsa ssa, IrStatement statement) =>
        statement.Node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment
        && IsPlain(assignment.Right)
        && assignment.Left switch {
            CpuFlagNode flag when IsStatusFlag(flag.FlagMask) =>
                AllMaskFlagsDefinedAndDead(ssa, statement, flag.FlagMask),
            FlagRegisterNode { DataType: { } dataType } when dataType == DataType.UINT32 =>
                DefinedStatusFlagsDead(ssa, statement),
            _ => false
        };

    private static bool AllMaskFlagsDefinedAndDead(IrSsa ssa, IrStatement statement, ushort mask) {
        foreach ((StatusFlags flag, IrSsaValue value) in IrFlagLiveness.StatusFlagValues) {
            if (((ushort)flag & mask) != 0 && !DefinedAndDead(ssa, statement, value)) {
                return false;
            }
        }
        return true;
    }

    private static bool DefinedStatusFlagsDead(IrSsa ssa, IrStatement statement) =>
        ssa.Definitions(statement).All(definition =>
            !IrFlagLiveness.IsStatusFlagValue(definition.Value) || !ssa.IsLive(definition.Version));

    /// <summary>The statement defines <paramref name="value"/> and the new version is never read live.</summary>
    private static bool DefinedAndDead(IrSsa ssa, IrStatement statement, IrSsaValue value) {
        foreach ((IrSsaValue defined, int version) in ssa.Definitions(statement)) {
            if (defined == value) {
                return !ssa.IsLive(version);
            }
        }
        return false;
    }

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
