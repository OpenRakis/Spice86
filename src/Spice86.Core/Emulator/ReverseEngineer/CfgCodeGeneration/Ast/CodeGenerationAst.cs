namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Collections.Generic;

/// <summary>Builds and inspects lowered code: statement sequences, the empty sequence, switch sections and negated conditions.</summary>
internal static class CodeGenerationAst {
    /// <summary>A new empty statement sequence.</summary>
    public static BlockNode Empty() => new();

    /// <summary>The statements of <paramref name="parts"/> in order; a <see cref="BlockNode"/> part contributes its statements.</summary>
    public static BlockNode Sequence(params IVisitableAstNode[] parts) => Sequence((IEnumerable<IVisitableAstNode>)parts);

    /// <inheritdoc cref="Sequence(IVisitableAstNode[])"/>
    public static BlockNode Sequence(IEnumerable<IVisitableAstNode> parts) {
        List<IVisitableAstNode> statements = [];
        foreach (IVisitableAstNode part in parts) {
            if (part is BlockNode block) {
                statements.AddRange(block.Statements);
            } else {
                statements.Add(part);
            }
        }
        return new BlockNode(statements.ToArray());
    }

    /// <summary>True when <paramref name="node"/> is a <see cref="BlockNode"/> without statements.</summary>
    public static bool IsEmpty(IVisitableAstNode node) => node is BlockNode { Statements.Count: 0 };

    /// <summary>A switch section whose body ends with <c>break;</c> exactly when it would otherwise complete normally.</summary>
    public static SwitchSection Section(int label, IVisitableAstNode body) {
        BlockNode statements = Sequence(body);
        if (!CompletesNormallyVisitor.CompletesNormally(statements)) {
            return new SwitchSection(label, statements);
        }
        return new SwitchSection(label, Sequence(statements, new BreakNode()));
    }

    /// <summary>The negation of a condition: the operand of a <c>!</c>, the opposite comparison, or <c>!</c> around the condition.</summary>
    public static ValueNode NegatedCondition(ValueNode condition) {
        if (condition is UnaryOperationNode { UnaryOperation: UnaryOperation.NOT } notNode) {
            return notNode.Value;
        }
        if (condition is BinaryOperationNode comparison && InvertedComparison(comparison.BinaryOperation) is BinaryOperation inverted) {
            return new BinaryOperationNode(DataType.BOOL, comparison.Left, inverted, comparison.Right);
        }
        if (condition is BinaryOperationNode { BinaryOperation: BinaryOperation.LOGICAL_AND } conjunction) {
            return new BinaryOperationNode(DataType.BOOL, NegatedCondition(conjunction.Left), BinaryOperation.LOGICAL_OR,
                NegatedCondition(conjunction.Right));
        }
        if (condition is BinaryOperationNode { BinaryOperation: BinaryOperation.LOGICAL_OR } disjunction) {
            return new BinaryOperationNode(DataType.BOOL, NegatedCondition(disjunction.Left), BinaryOperation.LOGICAL_AND,
                NegatedCondition(disjunction.Right));
        }
        return new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, condition);
    }

    /// <summary>
    /// The CFG node control reaches first when it enters <paramref name="statement"/>: the node of a boundary, the
    /// target of a label, the entry node of the first statement of a loop body; <c>null</c> for any other statement.
    /// </summary>
    public static ICfgNode? EntryNodeOf(IVisitableAstNode statement) => statement switch {
        InstructionBoundaryNode boundary => boundary.Node,
        LabelNode label => label.Target,
        InfiniteLoopNode loop => FirstEntryNode(loop.Body),
        DoWhileNode doWhile => FirstEntryNode(doWhile.Body),
        _ => null
    };

    private static ICfgNode? FirstEntryNode(BlockNode body) {
        if (body.Statements.Count == 0) {
            return null;
        }
        return EntryNodeOf(body.Statements[0]);
    }

    private static BinaryOperation? InvertedComparison(BinaryOperation operation) => operation switch {
        BinaryOperation.EQUAL => BinaryOperation.NOT_EQUAL,
        BinaryOperation.NOT_EQUAL => BinaryOperation.EQUAL,
        BinaryOperation.LESS_THAN => BinaryOperation.GREATER_THAN_OR_EQUAL,
        BinaryOperation.GREATER_THAN_OR_EQUAL => BinaryOperation.LESS_THAN,
        BinaryOperation.GREATER_THAN => BinaryOperation.LESS_THAN_OR_EQUAL,
        BinaryOperation.LESS_THAN_OR_EQUAL => BinaryOperation.GREATER_THAN,
        _ => null
    };
}
