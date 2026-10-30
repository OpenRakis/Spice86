namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;

using System.Linq;

/// <summary>
/// Simplifies <see cref="IrBranch"/> conditions - <c>false || x</c> to <c>x</c>, <c>true || x</c> and
/// <c>false && x</c> to a boolean constant, <c>true && x</c> to <c>x</c>, <c>!true</c> to false, and
/// constant comparisons folded with the known register bytes of <see cref="IrConstantFacts"/> - then
/// turns a branch whose condition is constant into an <see cref="IrJump"/> to the taken arm, keeping
/// that edge's <see cref="IrEdge.InsideInstruction"/> flag, and removes the blocks no longer
/// reachable from an entry. Passes may remove edges, never add one.
/// </summary>
internal static class IrBranchSimplification {
    /// <summary>Simplifies the branch conditions of <paramref name="method"/> and deletes the blocks
    /// left unreachable; returns how many branches folded to jumps and how many blocks were removed.</summary>
    public static (int BranchesRemoved, int BlocksRemoved) Apply(IrMethod method, IrConstantFacts facts) {
        int branches = 0;
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is not IrBranch branch) {
                continue;
            }
            ValueNode condition = Simplify(branch.Condition, block, facts);
            if (facts.TryEvaluate(condition, block, block.Statements.Count, out ulong constant)) {
                block.Terminator = new IrJump(branch.Origin,
                    constant != 0 ? branch.WhenTrue : branch.WhenFalse);
                branches++;
            } else if (!ReferenceEquals(condition, branch.Condition)) {
                block.Terminator = new IrBranch(branch.Origin, condition,
                    branch.WhenTrue, branch.WhenFalse);
            }
        }
        HashSet<IrBlock> reachable = ReachableBlocks(method);
        int removed = method.Blocks.RemoveAll(block => !reachable.Contains(block));
        return (branches, removed);
    }

    /// <summary>The blocks reachable from a method entry through the terminator edges.</summary>
    private static HashSet<IrBlock> ReachableBlocks(IrMethod method) {
        HashSet<IrBlock> reachable = [.. method.Entries.Select(entry => entry.Block)];
        Queue<IrBlock> worklist = new(reachable);
        while (worklist.Count > 0) {
            IrBlock block = worklist.Dequeue();
            foreach (IrEdge edge in block.Terminator.Edges) {
                if (!reachable.Add(edge.Target)) {
                    continue;
                }
                worklist.Enqueue(edge.Target);
            }
        }
        return reachable;
    }

    /// <summary><paramref name="node"/> with its boolean and comparison simplifications applied:
    /// a subexpression the facts evaluate to a constant folds into a <see cref="ConstantNode"/>,
    /// then the boolean identities collapse the result.</summary>
    private static ValueNode Simplify(ValueNode node, IrBlock block, IrConstantFacts facts) {
        if (node is ConstantNode) {
            return node;
        }
        if (facts.TryEvaluate(node, block, block.Statements.Count, out ulong constant)) {
            return new ConstantNode(node.DataType,
                ConstantEvaluation.Bits(node.DataType, constant));
        }
        switch (node) {
            case UnaryOperationNode { UnaryOperation: UnaryOperation.NOT } unary:
                ValueNode value = Simplify(unary.Value, block, facts);
                return ReferenceEquals(value, unary.Value) ? node
                    : new UnaryOperationNode(unary.DataType, UnaryOperation.NOT, value);
            case BinaryOperationNode { BinaryOperation: BinaryOperation.LOGICAL_OR } orExpression:
                return SimplifyLogical(orExpression, block, facts, true);
            case BinaryOperationNode { BinaryOperation: BinaryOperation.LOGICAL_AND } andExpression:
                return SimplifyLogical(andExpression, block, facts, false);
            case BinaryOperationNode binary:
                ValueNode left = Simplify(binary.Left, block, facts);
                ValueNode right = Simplify(binary.Right, block, facts);
                return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right)
                    ? node
                    : new BinaryOperationNode(binary.DataType, left, binary.BinaryOperation, right);
            default:
                return node;
        }
    }

    /// <summary><c>a || b</c> or <c>a && b</c> after each side is simplified, then the identities:
    /// for or <paramref name="shortCircuit"/> is true (<c>nonzero || x</c> and <c>x || nonzero</c>
    /// fold to true, <c>0 || x</c> to <c>x</c>); for and it is false (<c>0 && x</c> and <c>x && 0</c>
    /// fold to false, <c>nonzero && x</c> to <c>x</c>).</summary>
    private static ValueNode SimplifyLogical(BinaryOperationNode binary, IrBlock block,
        IrConstantFacts facts, bool shortCircuit) {
        ValueNode left = Simplify(binary.Left, block, facts);
        ValueNode right = Simplify(binary.Right, block, facts);
        if (CollapseLogical(left, right, shortCircuit) is { } collapsed) {
            return collapsed;
        }
        return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right) ? binary
            : new BinaryOperationNode(binary.DataType, left, binary.BinaryOperation, right);
    }

    /// <summary>The boolean identities on an <c>a || b</c> or <c>a && b</c> whose sides are already
    /// simplified: for <paramref name="shortCircuit"/> true (<c>||</c>) <c>nonzero || x</c> and
    /// <c>x || nonzero</c> fold to true and <c>0 || x</c> to <c>x</c>; for false (<c>&&</c>)
    /// <c>0 && x</c> and <c>x && 0</c> fold to false and <c>nonzero && x</c> to <c>x</c>.
    /// Shared with <see cref="IrConstants"/> so a condition simplifies the same way in both passes;
    /// null when no identity applies.</summary>
    internal static ValueNode? CollapseLogical(ValueNode left, ValueNode right, bool shortCircuit) {
        if (left is ConstantNode leftConstant) {
            return (leftConstant.Value != 0) == shortCircuit
                ? new ConstantNode(DataType.BOOL, shortCircuit ? 1UL : 0UL)
                : right;
        }
        if (right is ConstantNode rightConstant) {
            return (rightConstant.Value != 0) == shortCircuit
                ? new ConstantNode(DataType.BOOL, shortCircuit ? 1UL : 0UL)
                : left;
        }
        return null;
    }
}
