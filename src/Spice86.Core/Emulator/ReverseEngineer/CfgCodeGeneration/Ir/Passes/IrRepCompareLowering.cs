namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;

/// <summary>
/// A <c>repe</c>/<c>repne cmps</c>/<c>scas</c> whose
/// <c>AluN.Sub(left, right)</c> statement feeds no live flag use but the loop's exit latch is rewritten to
/// compare the operands directly - the call becomes an <c>equal_&lt;suffix&gt;</c> boolean and the latch
/// condition tests it instead of <c>ZeroFlag</c>.
/// </summary>
internal static class IrRepCompareLowering {
    /// <summary>Rewrites every rewritable <see cref="IrInstructionShape.RepCompare"/> instruction.</summary>
    public static void Apply(IrMethod method, IrSsa ssa) {
        Dictionary<ICfgNode, List<IrOrigins.Located>> byOrigin = IrOrigins.ByOrigin(method);
        foreach ((ICfgNode origin, IrInstructionShape shape) in method.Shapes) {
            if (shape != IrInstructionShape.RepCompare
                || !byOrigin.TryGetValue(origin, out List<IrOrigins.Located>? statements)
                || !IrOrigins.Rewritable(statements, origin)) {
                continue;
            }
            if (SubOf(statements) is not { } sub
                || LatchOf(method, origin) is not (IrBlock LatchBlock, IrBranch Branch, bool OnEqual) latch
                || !FlagsOnlyFeedTheLatch(ssa, sub.Statement, latch.Branch)) {
                continue;
            }
            method.LocalSuffixes.TryGetValue(origin, out string? suffix);
            VariableDeclarationNode equal = new(DataType.BOOL,
                $"equal_{suffix ?? origin.Address.Offset.ToString("X4")}",
                new BinaryOperationNode(DataType.BOOL, sub.Left, BinaryOperation.EQUAL, sub.Right));
            ValueNode stop = latch.OnEqual
                ? equal.Reference
                : new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, equal.Reference);
            IrStatement replacement = IrStatement.Create(equal, origin);
            ssa.Replace(sub.Block.Statements[sub.Index], replacement);
            sub.Block.Statements[sub.Index] = replacement;
            latch.Block.Terminator = new IrBranch(latch.Branch.Origin, stop,
                latch.Branch.WhenTrue, latch.Branch.WhenFalse);
        }
    }

    /// <summary>The <c>AluN.Sub(left, right)</c> used as a statement, uniquely found, or <c>null</c>.</summary>
    private static (IrBlock Block, int Index, IrStatement Statement, ValueNode Left, ValueNode Right)?
        SubOf(IReadOnlyList<IrOrigins.Located> statements) {
        (IrBlock, int, IrStatement, ValueNode, ValueNode)? found = null;
        foreach (IrOrigins.Located located in statements) {
            if (located.Statement.Node is not AluOperationNode {
                    Operation: AluOperation.Sub,
                    Call.CallNode.Arguments: [ValueNode, ValueNode]
                } node) {
                continue;
            }
            if (found is not null) {
                return null;
            }
            found = (located.Block, located.Index, located.Statement,
                (ValueNode)node.Call.CallNode.Arguments[0],
                (ValueNode)node.Call.CallNode.Arguments[1]);
        }
        return found;
    }

    /// <summary>
    /// The branch testing <c>ZeroFlag</c> of the instruction - its exit latch - and whether the loop
    /// stops when the flag is set (<c>ZeroFlag</c> or <c>ZeroFlag == 1</c>) rather than clear
    /// (<c>!ZeroFlag</c> or <c>ZeroFlag == 0</c>).
    /// </summary>
    private static (IrBlock Block, IrBranch Branch, bool OnEqual)? LatchOf(IrMethod method,
        ICfgNode origin) {
        (IrBlock, IrBranch, bool)? found = null;
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is not IrBranch { Origin: { } branchOrigin } branch
                || !branchOrigin.Equals(origin) || OnEqual(branch.Condition) is not { } onEqual) {
                continue;
            }
            if (found is not null) {
                return null;
            }
            found = (block, branch, onEqual);
        }
        return found;
    }

    /// <summary>Whether the condition exits the loop when <c>ZeroFlag</c> is set, or <c>null</c> when it
    /// does not test <c>ZeroFlag</c>.</summary>
    private static bool? OnEqual(ValueNode condition) => condition switch {
        CpuFlagNode { FlagMask: Flags.Zero } => true,
        UnaryOperationNode { UnaryOperation: UnaryOperation.NOT, Value: CpuFlagNode { FlagMask: Flags.Zero } }
            => false,
        BinaryOperationNode {
            BinaryOperation: BinaryOperation.EQUAL,
            Left: CpuFlagNode { FlagMask: Flags.Zero },
            Right: ConstantNode zero
        } => zero.Value != 0,
        _ => null
    };

    /// <summary>Every status flag the Sub defines is used only by the latch branch condition.</summary>
    private static bool FlagsOnlyFeedTheLatch(IrSsa ssa, IrStatement sub, IrBranch latch) {
        foreach ((IrSsaValue value, int version) in ssa.Definitions(sub)) {
            if (!IrFlagLiveness.IsStatusFlagValue(value)) {
                continue;
            }
            foreach (IrSsaUse use in ssa.LiveUses(version)) {
                if (!ReferenceEquals(use.Terminator, latch)) {
                    return false;
                }
            }
        }
        return true;
    }
}
