namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>Constant folding of IR AST: constant subexpressions to one constant, identity terms
/// (<c>0 + x</c>, <c>x + 0</c>, ...) removed, a known condition of a
/// <see cref="ConditionalValueNode"/> or <see cref="IfElseNode"/> resolved, and variable constants
/// substituted from a name table. Passes that rebuild statements fold their output with it: the
/// rewrite only touches operands, so a statement's effects are unchanged.</summary>
internal static class IrConstantFolder {
    internal static readonly IReadOnlyDictionary<string, ConstantNode> NoVariableConstants =
        new Dictionary<string, ConstantNode>();

    /// <summary>Folds constant subexpressions to one constant and removes identity terms
    /// (<c>0 + x</c>, <c>x + 0</c>, ...). Passes that rebuild statements after this pass ran fold
    /// their output with it. A call argument only folds when it reads a variable: a plain
    /// <c>InstructionFieldNode</c> argument keeps its shape so the statement printer can render
    /// <c>x += 1</c>. Stack, bound, bit-scan, signature and external-code calls keep their arguments
    /// as lowered.</summary>
    internal static IVisitableAstNode Fold(IVisitableAstNode node,
        IReadOnlyDictionary<string, ConstantNode> variableConstants) {
        switch (node) {
            case BinaryOperationNode binary:
                return FoldBinary(binary, variableConstants);
            case UnaryOperationNode or TypeConversionNode:
                return FoldValue(
                    IrAstChildren.MapChildren(node, child => Fold(child, variableConstants)),
                    variableConstants);
            case ConditionalValueNode conditional:
                return FoldConditional(conditional, variableConstants);
            case AluOperationNode or AluStatementNode or PortWriteCallNode or PortReadCallNode:
                return IrAstChildren.MapChildren(node,
                    child => ContainsVariableReference(child) ? Fold(child, variableConstants) : child);
            case StackCallNode or StackCallValueNode or BoundCheckCallNode or BitScanCallNode
                or ProcedureCallHelperNode or InterruptCallHelperNode or CallbackHelperNode
                or CallbackMovedCsIpHelperNode or SignatureMatchesHelperNode:
                return node;
            case MethodCallValueNode or MethodCallNode:
                return IrAstChildren.MapChildren(node, child => Fold(child, variableConstants));
            case IfElseNode ifElse:
                IVisitableAstNode condition = FoldCondition(ifElse.Condition, variableConstants);
                return ReferenceEquals(condition, ifElse.Condition) ? node
                    : new IfElseNode((ValueNode)condition, ifElse.TrueCase, ifElse.FalseCase);
            case WhileNode whileNode:
                return FoldWhile(whileNode, variableConstants);
            case WhileWithExitNode whileWithExit:
                return FoldWhileWithExit(whileWithExit, variableConstants);
            case SegmentedPointerNode or AbsolutePointerNode or SegmentedAddressNode
                or VariableDeclarationNode:
                return IrAstChildren.MapChildren(node,
                    child => Fold(child, variableConstants));
            default:
                return node;
        }
    }

    private static IVisitableAstNode FoldBinary(BinaryOperationNode binary,
        IReadOnlyDictionary<string, ConstantNode> variableConstants) {
        BinaryOperationNode rebuiltBinary = (BinaryOperationNode)IrAstChildren.MapChildren(binary,
            child => Fold(child, variableConstants));
        if (rebuiltBinary.BinaryOperation is BinaryOperation.LOGICAL_OR or BinaryOperation.LOGICAL_AND
            && IrBranchSimplification.CollapseLogical(rebuiltBinary.Left, rebuiltBinary.Right,
                rebuiltBinary.BinaryOperation == BinaryOperation.LOGICAL_OR) is { } collapsedLogical) {
            return collapsedLogical;
        }
        return FoldValue(FoldIdentity(rebuiltBinary), variableConstants);
    }

    private static IVisitableAstNode FoldWhile(WhileNode whileNode,
        IReadOnlyDictionary<string, ConstantNode> variableConstants) {
        IVisitableAstNode whileCondition = FoldCondition(whileNode.Condition, variableConstants);
        BlockNode whileBody = FoldBody(whileNode.Body);
        return ReferenceEquals(whileCondition, whileNode.Condition)
            && ReferenceEquals(whileBody, whileNode.Body) ? whileNode
            : new WhileNode((ValueNode)whileCondition, whileBody);
    }

    private static IVisitableAstNode FoldWhileWithExit(WhileWithExitNode whileWithExit,
        IReadOnlyDictionary<string, ConstantNode> variableConstants) {
        IVisitableAstNode enterCondition = FoldCondition(whileWithExit.Condition, variableConstants);
        IVisitableAstNode exitCondition = FoldCondition(whileWithExit.ExitCondition, variableConstants);
        BlockNode exitBody = FoldBody(whileWithExit.Body);
        return ReferenceEquals(enterCondition, whileWithExit.Condition)
            && ReferenceEquals(exitCondition, whileWithExit.ExitCondition)
            && ReferenceEquals(exitBody, whileWithExit.Body) ? whileWithExit
            : new WhileWithExitNode((ValueNode)enterCondition, exitBody,
                (ValueNode)exitCondition);
    }

    private static IVisitableAstNode FoldConditional(ConditionalValueNode conditional,
        IReadOnlyDictionary<string, ConstantNode> variableConstants) {
        ConditionalValueNode foldedConditional = (ConditionalValueNode)IrAstChildren.MapChildren(
            conditional, child => Fold(child, variableConstants));
        if (ConstantEvaluation.TryEvaluate(foldedConditional.Condition,
                leaf => LeafConstant(leaf, variableConstants), out ulong taken)) {
            return taken != 0 ? foldedConditional.WhenTrue : foldedConditional.WhenFalse;
        }
        if (foldedConditional.WhenTrue.Equals(foldedConditional.WhenFalse)) {
            return foldedConditional.WhenTrue;
        }
        ConditionalValueNode candidate = foldedConditional;
        if (candidate.Condition is UnaryOperationNode { UnaryOperation: UnaryOperation.NOT } negation) {
            candidate = new ConditionalValueNode(candidate.DataType, negation.Value,
                candidate.WhenFalse, candidate.WhenTrue);
        }
        if (candidate.WhenTrue is ConstantNode { Value: 1 }
            && candidate.WhenFalse is ConstantNode { Value: 0 }) {
            return new TypeConversionNode(candidate.DataType, candidate.Condition);
        }
        return candidate;
    }

    /// <summary>Folds the statements of a loop body: a variable constant from before the loop can be
    /// stale on a later iteration, so the body folds with the empty variable table.</summary>
    private static BlockNode FoldBody(BlockNode body) =>
        (BlockNode)IrAstChildren.MapChildren(body, statement => Fold(statement, NoVariableConstants));

    /// <summary>Folds the additive constant chains <see cref="IrExpressionPropagation"/> merging
    /// creates in every statement and terminator expression of <paramref name="method"/>. Only
    /// constant operands combine, so a rewritten statement reads and writes the same
    /// <see cref="IrValues"/> and <see cref="IrTokens"/>, cannot newly fault
    /// (<see cref="IrEffects.MayFault"/>) or deliver interrupts
    /// (<see cref="IrEffects.DeliversInterrupts"/>), and the SSA versions it holds are the same
    /// (<see cref="IrSsa.SameVersions"/>): the statement is replaced through
    /// <see cref="IrSsa.Replace"/> so its site stays.</summary>
    internal static void FoldAdditiveConstants(IrMethod method, IrSsa ssa) {
        foreach (IrBlock block in method.Blocks) {
            for (int i = 0; i < block.Statements.Count; i++) {
                IrStatement statement = block.Statements[i];
                IVisitableAstNode node = FoldAdditiveTerms(statement.Node);
                if (ReferenceEquals(node, statement.Node)) {
                    continue;
                }
                IrStatement replacement = IrStatement.Create(node, statement.Origin);
                block.Statements[i] = replacement;
                if (ssa.HasSite(statement)) {
                    ssa.Replace(statement, replacement);
                }
            }
            IrTerminator terminator = FoldAdditiveTerminator(block.Terminator);
            if (!ReferenceEquals(terminator, block.Terminator)) {
                ssa.ReplaceTerminator(block.Terminator, terminator);
                block.Terminator = terminator;
            }
        }
    }

    private static IrTerminator FoldAdditiveTerminator(IrTerminator terminator) => terminator switch {
        IrBranch branch => FoldAdditiveTerms(branch.Condition) is ValueNode condition
            && !ReferenceEquals(condition, branch.Condition)
            ? new IrBranch(branch.Origin, condition, branch.WhenTrue, branch.WhenFalse)
            : terminator,
        IrSwitch @switch => FoldAdditiveTerms(@switch.Value) is ValueNode value
            && !ReferenceEquals(value, @switch.Value)
            ? new IrSwitch(@switch.Origin, value, @switch.Cases, @switch.Default)
            : terminator,
        IrExit exit => FoldAdditiveTerms(exit.Transfer) is { } transfer
            && !ReferenceEquals(transfer, exit.Transfer)
            ? new IrExit(exit.Origin, transfer)
            : terminator,
        _ => terminator
    };

    /// <summary><paramref name="node"/> with every additive constant chain folded: a <c>+/-</c> of
    /// two constants to one constant, <c>(x +/- c1) +/- c2</c> to <c>x</c> plus or minus the
    /// combined term, reduced modulo the node's width.</summary>
    internal static IVisitableAstNode FoldAdditiveTerms(IVisitableAstNode node) {
        IVisitableAstNode folded = node is BinaryOperationNode binary ? FoldAdditiveTerms(binary) : node;
        return IrAstChildren.MapChildren(folded, FoldAdditiveTerms);
    }

    /// <summary>Folds a binary <c>+/-</c> of two constants into the constant of the node's width, and
    /// the constant terms of <c>(x +/- c1) +/- c2</c> into one, reduced modulo the width of the
    /// node's type; the combined term becomes the smaller of <c>x + c</c> and
    /// <c>x - (2^w - c)</c>.</summary>
    private static IVisitableAstNode FoldAdditiveTerms(BinaryOperationNode node) {
        if (node.BinaryOperation is not (BinaryOperation.PLUS or BinaryOperation.MINUS)) {
            return node;
        }
        int bits = node.DataType.BitWidth switch {
            BitWidth.BYTE_8 => 8,
            BitWidth.WORD_16 => 16,
            BitWidth.DWORD_32 => 32,
            BitWidth.QWORD_64 => 64,
            _ => 0
        };
        if (bits == 0) {
            return node;
        }
        ulong mask = bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;
        if (ConstantEvaluation.Resolved(node.Left)?.Value is { } leftConstant
            && ConstantEvaluation.Resolved(node.Right)?.Value is { } rightConstant) {
            ulong result = node.BinaryOperation == BinaryOperation.PLUS
                ? leftConstant + rightConstant & mask
                : leftConstant - rightConstant & mask;
            return new ConstantNode(node.DataType, result);
        }
        if (node.Left is not BinaryOperationNode inner
            || inner.BinaryOperation is not (BinaryOperation.PLUS or BinaryOperation.MINUS)
            || inner.DataType != node.DataType
            || ConstantEvaluation.Resolved(inner.Right)?.Value is not { } first
            || ConstantEvaluation.Resolved(node.Right)?.Value is not { } second) {
            return node;
        }
        long combined = (inner.BinaryOperation == BinaryOperation.PLUS ? (long)first : -(long)first)
            + (node.BinaryOperation == BinaryOperation.PLUS ? (long)second : -(long)second);
        ulong remainder = (ulong)combined & mask;
        if (remainder == 0) {
            return inner.Left;
        }
        ulong negation = ~remainder + 1 & mask;
        DataType constantType = DataType.UnsignedFromBitWidth(node.DataType.BitWidth);
        return remainder <= negation
            ? new BinaryOperationNode(node.DataType, inner.Left, BinaryOperation.PLUS,
                new ConstantNode(constantType, remainder))
            : new BinaryOperationNode(node.DataType, inner.Left, BinaryOperation.MINUS,
                new ConstantNode(constantType, negation));
    }

    private static bool ContainsVariableReference(IVisitableAstNode node) =>
        IrAstChildren.Descendants(node).Any(descendant => descendant is VariableReferenceNode);

    private static IVisitableAstNode FoldCondition(IVisitableAstNode condition,
        IReadOnlyDictionary<string, ConstantNode> variableConstants) {
        IVisitableAstNode folded = Fold(condition, variableConstants);
        return IrConstants.ContainsStateRead(folded) ? folded : condition;
    }

    /// <summary><c>0 + x</c>, <c>x + 0</c>, <c>x - 0</c>, <c>x | 0</c>, <c>x ^ 0</c>, <c>x << 0</c> and
    /// <c>x >> 0</c> are <paramref name="binary"/> simplified to <c>x</c>: substitution can produce zero
    /// terms, and they carry no information. The zero term is dropped only when <c>x</c> has the type of
    /// the binary; a narrower or differently signed <c>x</c> keeps the add so the expression stays typed
    /// by the binary.</summary>
    private static IVisitableAstNode FoldIdentity(BinaryOperationNode binary) {
        if (IsZero(binary.Left) && binary.BinaryOperation == BinaryOperation.PLUS
            && binary.Right.DataType == binary.DataType) {
            return binary.Right;
        }
        if (IsZero(binary.Right)
            && binary.BinaryOperation is BinaryOperation.PLUS or BinaryOperation.MINUS
                or BinaryOperation.BITWISE_OR or BinaryOperation.BITWISE_XOR
                or BinaryOperation.LEFT_SHIFT or BinaryOperation.RIGHT_SHIFT
            && binary.Left.DataType == binary.DataType) {
            return binary.Left;
        }
        return binary;
    }

    private static bool IsZero(ValueNode node) => ConstantEvaluation.Resolved(node) is { Value: 0 };

    private static IVisitableAstNode FoldValue(IVisitableAstNode node,
        IReadOnlyDictionary<string, ConstantNode> variableConstants) {
        if (node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN }
            || WrapsVariableReference(node)
            || !ConstantEvaluation.TryEvaluate(node,
                leaf => LeafConstant(leaf, variableConstants), out ulong value)) {
            return node;
        }
        DataType type = ((ValueNode)node).DataType;
        return new ConstantNode(type, ConstantEvaluation.Bits(type, value));
    }

    /// <summary>A conversion or unary wrapping a bare variable reference keeps it: a lowerer pattern
    /// (division) matches those wrappers structurally.</summary>
    private static bool WrapsVariableReference(IVisitableAstNode node) => node switch {
        TypeConversionNode conversion => conversion.Value is VariableReferenceNode,
        UnaryOperationNode unary => unary.Value is VariableReferenceNode,
        _ => false
    };

    private static ulong? LeafConstant(IVisitableAstNode leaf,
        IReadOnlyDictionary<string, ConstantNode> variableConstants) =>
        leaf is VariableReferenceNode reference
        && variableConstants.TryGetValue(reference.VariableName, out ConstantNode? constant)
            ? ConstantEvaluation.SemanticValue(constant.DataType, constant.Value)
            : null;
}
