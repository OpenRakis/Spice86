namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Collections.Generic;
using System.Linq;

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

    /// <summary>The statements <see cref="Sequence(IVisitableAstNode[])"/> would give for <paramref name="node"/> alone, without copying: a block's statements, or the node itself.</summary>
    public static IReadOnlyList<IVisitableAstNode> StatementsOf(IVisitableAstNode node) =>
        node is BlockNode block ? block.Statements : [node];

    /// <summary>
    /// The statements directly inside <paramref name="node"/> when it is a statement container (block, instruction boundary,
    /// if / else, loops, switch sections and default, CPU fault try): each child body, or the statements of a block. Empty for
    /// any other node.
    /// </summary>
    public static IEnumerable<IVisitableAstNode> ChildStatements(IVisitableAstNode node) => node switch {
        BlockNode block => block.Statements,
        InstructionBoundaryNode boundary => [boundary.Body],
        IfElseNode ifElse => [ifElse.TrueCase, ifElse.FalseCase],
        WhileNode whileNode => [whileNode.Body],
        InfiniteLoopNode loop => [loop.Body],
        DoWhileNode doWhile => [doWhile.Body],
        SwitchNode switchNode => [.. switchNode.Sections.Select(section => section.Body), switchNode.Default],
        CpuFaultTryCatchNode tryCatch => [tryCatch.Body],
        _ => []
    };

    /// <summary>Every <see cref="GotoNode"/> of <paramref name="node"/>, in print order.</summary>
    public static IEnumerable<GotoNode> Gotos(IVisitableAstNode node) =>
        node is GotoNode jump ? [jump] : ChildStatements(node).SelectMany(Gotos);

    /// <summary>True when <paramref name="node"/> or one of its nested statements is a <typeparamref name="T"/>.</summary>
    public static bool ContainsNode<T>(IVisitableAstNode node) =>
        node is T || ChildStatements(node).Any(ContainsNode<T>);

    /// <summary>
    /// <paramref name="node"/> rebuilt with <paramref name="rewriteBlock"/> applied to each of its child bodies (see
    /// <see cref="ChildStatements"/>); a block is passed to <paramref name="rewriteBlock"/> itself. Any other node is returned as it is.
    /// </summary>
    public static IVisitableAstNode WithRewrittenBodies(IVisitableAstNode node, Func<BlockNode, BlockNode> rewriteBlock) => node switch {
        BlockNode block => rewriteBlock(block),
        InstructionBoundaryNode boundary => boundary with { Body = rewriteBlock(boundary.Body) },
        IfElseNode ifElse => new IfElseNode(ifElse.Condition, rewriteBlock(Sequence(ifElse.TrueCase)), rewriteBlock(Sequence(ifElse.FalseCase))),
        WhileNode whileNode => new WhileNode(whileNode.Condition, rewriteBlock(whileNode.Body)),
        InfiniteLoopNode loop => loop with { Body = rewriteBlock(loop.Body) },
        DoWhileNode doWhile => doWhile with { Body = rewriteBlock(doWhile.Body) },
        SwitchNode switchNode => switchNode with {
            Sections = switchNode.Sections.Select(section => section with { Body = rewriteBlock(section.Body) }).ToList(),
            Default = rewriteBlock(switchNode.Default)
        },
        CpuFaultTryCatchNode tryCatch => tryCatch with { Body = rewriteBlock(tryCatch.Body) },
        _ => node
    };

    /// <summary>True when <paramref name="node"/> reads only registers, flags and other CPU state: it can move out of a boundary.</summary>
    public static bool ReadsOnlyMachineState(ValueNode node) => node switch {
        CpuFlagNode or RegisterNode or ConstantNode or InstructionFieldNode => true,
        UnaryOperationNode unary => ReadsOnlyMachineState(unary.Value),
        ExplicitCastNode { Value: ValueNode inner } => ReadsOnlyMachineState(inner),
        BinaryOperationNode binary => ReadsOnlyMachineState(binary.Left) && ReadsOnlyMachineState(binary.Right),
        TypeConversionNode conversion => ReadsOnlyMachineState(conversion.Value),
        SegmentedPointerNode pointer => ReadsOnlyMachineState(pointer.Segment) && ReadsOnlyMachineState(pointer.Offset)
            && (pointer.DefaultSegment is null || ReadsOnlyMachineState(pointer.DefaultSegment)),
        _ => false
    };

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
        WhileNode loop => FirstEntryNode(loop.Body),
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
