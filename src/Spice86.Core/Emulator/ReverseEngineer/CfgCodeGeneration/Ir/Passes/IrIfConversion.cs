namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Linq;

/// <summary>
/// Folds a value-choice branch inside one instruction into one conditional assignment:
/// <c>if (c) x = a else x = b</c> becomes <c>x = c ? a : b</c> in the branching block and the arm
/// blocks are removed. The conditions, in the terms of the stored effects model:
/// <list type="bullet">
/// <item>every edge involved (both edges of the <see cref="IrBranch"/>, the arm's jump to the join) is
/// an inside edge: the whole shape is the control flow one instruction lowers to, so the new
/// <see cref="IrJump"/> to the join is a legal inside edge of the same origin;</item>
/// <item>an arm block is not an entry and appears in no <see cref="IrMethod.StructuralSuccessors"/>
/// edge, has exactly one incoming edge, ends with an <see cref="IrJump"/> to the shared join and holds
/// at most one statement; an arm with no block at all is an empty inside edge straight to the
/// join;</item>
/// <item>each arm statement is one <see cref="BinaryOperation.ASSIGN"/> to a
/// <see cref="RegisterNode"/> (never a segment register), a <see cref="CpuFlagNode"/> or a
/// <see cref="VariableReferenceNode"/> of the same instruction origin, with
/// <see cref="IrEffects.MayFault"/> false, no <see cref="IrTokens"/> read or write and no
/// <see cref="IrEffects.DeliversInterrupts"/>: moving it onto both paths can then introduce no fault,
/// side effect or interrupt the branch used to guard;</item>
/// <item>the two arms assign the same target with the same data type, and the arm value is a pure
/// value under <see cref="IrVerifier.IsPureArm"/> (no memory read, call or anything that can fault,
/// so evaluating it on the other path is a no-op) of at most <see cref="MaxArmNodes"/> AST
/// nodes;</item>
/// <item>the condition is not itself a <see cref="ConditionalValueNode"/>: the expression printer has
/// no form for a nested select in a condition.</item>
/// </list>
/// The emptied join is left with a single predecessor; <see cref="IrJumpThreading"/>, which runs
/// next, removes it.
/// </summary>
internal static class IrIfConversion {
    /// <summary>The largest arm value, in AST nodes, the pass moves into a conditional value.</summary>
    internal const int MaxArmNodes = 3;

    /// <summary>Converts every value-choice branch of <paramref name="method"/>; returns how many.</summary>
    public static int Apply(IrMethod method) {
        Dictionary<IrBlock, int> incoming = IncomingEdgeCounts(method);
        HashSet<IrBlock> pinned = PinnedBlocks(method);
        HashSet<IrBlock> removed = [];
        int converted = 0;
        foreach (IrBlock block in method.Blocks.ToArray()) {
            if (removed.Contains(block) || block.Terminator is not IrBranch branch) {
                continue;
            }
            if (TryShape(block, branch, incoming, pinned) is not { } shape
                || TryConversion(branch, shape) is not { } conversion) {
                continue;
            }
            block.Statements.Add(conversion);
            block.Terminator = new IrJump(branch.Origin, new IrEdge(shape.Join, true));
            removed.UnionWith(shape.ArmBlocks);
            converted++;
        }
        method.Blocks.RemoveAll(removed.Contains);
        return converted;
    }

    /// <summary>How many edges of every terminator target each block; an edge listed twice counts
    /// twice.</summary>
    private static Dictionary<IrBlock, int> IncomingEdgeCounts(IrMethod method) {
        Dictionary<IrBlock, int> incoming = [];
        foreach (IrBlock block in method.Blocks) {
            foreach (IrEdge edge in block.Terminator.Edges) {
                incoming[edge.Target] = incoming.GetValueOrDefault(edge.Target) + 1;
            }
        }
        return incoming;
    }

    /// <summary>The blocks a conversion may never remove: the entry blocks and every key and value
    /// of <see cref="IrMethod.StructuralSuccessors"/>.</summary>
    private static HashSet<IrBlock> PinnedBlocks(IrMethod method) {
        HashSet<IrBlock> pinned = [.. method.Entries.Select(entry => entry.Block)];
        foreach (KeyValuePair<IrBlock, List<IrBlock>> pair in method.StructuralSuccessors) {
            pinned.Add(pair.Key);
            pinned.UnionWith(pair.Value);
        }
        return pinned;
    }

    /// <summary>The arm-and-join shape of a convertible branch, or <see langword="null"/> when
    /// <paramref name="branch"/> is not a value choice of one instruction.</summary>
    private static Shape? TryShape(IrBlock block, IrBranch branch,
        IReadOnlyDictionary<IrBlock, int> incoming, HashSet<IrBlock> pinned) {
        if (branch.Condition is ConditionalValueNode
            || !branch.WhenTrue.InsideInstruction || !branch.WhenFalse.InsideInstruction) {
            return null;
        }
        Arm? trueArm = TryArm(branch.WhenTrue.Target, block, incoming, pinned);
        Arm? falseArm = TryArm(branch.WhenFalse.Target, block, incoming, pinned);
        if (trueArm is not null && falseArm is not null) {
            if (!ReferenceEquals(trueArm.Block, falseArm.Block)
                && ReferenceEquals(trueArm.Join, falseArm.Join)) {
                return new Shape(trueArm.Join, trueArm.Block.Statements.FirstOrDefault(),
                    falseArm.Block.Statements.FirstOrDefault(), [trueArm.Block, falseArm.Block]);
            }
            return null;
        }
        if (trueArm is not null && ReferenceEquals(trueArm.Join, branch.WhenFalse.Target)) {
            return new Shape(trueArm.Join, trueArm.Block.Statements.FirstOrDefault(), null,
                [trueArm.Block]);
        }
        if (falseArm is not null && ReferenceEquals(falseArm.Join, branch.WhenTrue.Target)) {
            return new Shape(falseArm.Join, null, falseArm.Block.Statements.FirstOrDefault(),
                [falseArm.Block]);
        }
        return null;
    }

    /// <summary><paramref name="candidate"/> as an arm of <paramref name="block"/>:
    /// not the branch itself, not pinned, exactly one incoming edge, at most one statement and an
    /// <see cref="IrJump"/> whose inside edge lands on a join that is neither itself nor the branch
    /// block; <see langword="null"/> when the candidate does not qualify.</summary>
    private static Arm? TryArm(IrBlock candidate, IrBlock block,
        IReadOnlyDictionary<IrBlock, int> incoming, HashSet<IrBlock> pinned) {
        if (ReferenceEquals(candidate, block) || pinned.Contains(candidate)
            || incoming.GetValueOrDefault(candidate) != 1 || candidate.Statements.Count > 1) {
            return null;
        }
        if (candidate.Terminator is not IrJump jump || !jump.Edge.InsideInstruction
            || ReferenceEquals(jump.Edge.Target, candidate)
            || ReferenceEquals(jump.Edge.Target, block)) {
            return null;
        }
        return new Arm(candidate, jump.Edge.Target);
    }

    /// <summary>The conditional assignment the arm statements of <paramref name="shape"/> fold to,
    /// or <see langword="null"/> when they are not one assignment to the same pure-value
    /// target.</summary>
    private static IrStatement? TryConversion(IrBranch branch, Shape shape) {
        List<IrStatement> statements = new(2);
        if (shape.TrueStatement is { } trueStatement) {
            statements.Add(trueStatement);
        }
        if (shape.FalseStatement is { } falseStatement) {
            statements.Add(falseStatement);
        }
        if (statements.Count == 0
            || statements.Any(statement => !IsValueChoice(statement, branch.Origin))) {
            return null;
        }
        BinaryOperationNode assignment = (BinaryOperationNode)statements[0].Node;
        if (statements.Count == 2) {
            BinaryOperationNode other = (BinaryOperationNode)statements[1].Node;
            if (!assignment.Left.Equals(other.Left) || assignment.DataType != other.DataType) {
                return null;
            }
        }
        ValueNode whenTrue = ArmValue(shape.TrueStatement, assignment.Left);
        ValueNode whenFalse = ArmValue(shape.FalseStatement, assignment.Left);
        return IrStatement.Create(new BinaryOperationNode(assignment.DataType, assignment.Left,
            BinaryOperation.ASSIGN, new ConditionalValueNode(assignment.Left.DataType,
                branch.Condition, whenTrue, whenFalse)), branch.Origin);
    }

    /// <summary>The value one side of the choice contributes: the arm statement's assigned value, or
    /// the target itself for an empty arm (the triangle's keep-old-value side).</summary>
    private static ValueNode ArmValue(IrStatement? statement, ValueNode target) {
        if (statement is null) {
            return target;
        }
        return ((BinaryOperationNode)statement.Node).Right;
    }

    /// <summary>Whether <paramref name="statement"/> is a value choice of
    /// <paramref name="origin"/>: one assignment to a register, flag or local whose value is a
    /// bounded pure expression and whose stored effects cannot fault, touch a token or deliver an
    /// interrupt.</summary>
    private static bool IsValueChoice(IrStatement statement, ICfgNode origin) =>
        statement.Node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assign
        && IsChoiceTarget(assign.Left)
        && IrVerifier.IsPureArm(assign.Right)
        && IrAstChildren.Descendants(assign.Right).Count() <= MaxArmNodes
        && ReferenceEquals(statement.Origin, origin)
        && !statement.Effects.MayFault
        && statement.Effects.TokenReads == IrTokens.None
        && statement.Effects.TokenWrites == IrTokens.None
        && !statement.Effects.DeliversInterrupts;

    /// <summary>The left side a value choice may write: a general register, a CPU flag or a local -
    /// never a memory location or a segment register.</summary>
    private static bool IsChoiceTarget(ValueNode left) =>
        left is CpuFlagNode or VariableReferenceNode
        || left is RegisterNode and not SegmentRegisterNode;

    /// <summary>An arm block and the join its <see cref="IrJump"/> lands on.</summary>
    private sealed record Arm(IrBlock Block, IrBlock Join);

    /// <summary>A resolved value choice: the join the arms meet at, the at-most-one statement of
    /// each side (<see langword="null"/> for an empty side) and the arm blocks to remove.</summary>
    private sealed record Shape(IrBlock Join, IrStatement? TrueStatement, IrStatement? FalseStatement,
        List<IrBlock> ArmBlocks);
}
