namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

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
        PreTestLoopNode preTestLoop => [preTestLoop.Body],
        InfiniteLoopNode loop => [loop.Body],
        DoWhileNode doWhile => [doWhile.Body],
        SwitchNode switchNode => [.. switchNode.Sections.Select(section => section.Body), switchNode.Default],
        CpuFaultTryCatchNode tryCatch => [tryCatch.Body],
        _ => []
    };

    /// <summary>Every <see cref="GotoNode"/> of <paramref name="node"/>, in print order.</summary>
    public static IEnumerable<GotoNode> Gotos(IVisitableAstNode node) {
        IEnumerable<IVisitableAstNode> roots = [node];
        Stack<IEnumerator<IVisitableAstNode>> pending = new();
        pending.Push(roots.GetEnumerator());
        try {
            while (pending.Count > 0) {
                IEnumerator<IVisitableAstNode> top = pending.Peek();
                if (!top.MoveNext()) {
                    pending.Pop().Dispose();
                    continue;
                }
                IVisitableAstNode current = top.Current;
                if (current is GotoNode jump) {
                    yield return jump;
                } else {
                    pending.Push(ChildStatements(current).GetEnumerator());
                }
            }
        } finally {
            while (pending.Count > 0) {
                pending.Pop().Dispose();
            }
        }
    }

    /// <summary>True when <paramref name="node"/> or one of its nested statements is a <typeparamref name="T"/>.</summary>
    public static bool ContainsNode<T>(IVisitableAstNode node) {
        Stack<IVisitableAstNode> pending = new();
        pending.Push(node);
        while (pending.Count > 0) {
            IVisitableAstNode current = pending.Pop();
            if (current is T) {
                return true;
            }
            foreach (IVisitableAstNode child in ChildStatements(current)) {
                pending.Push(child);
            }
        }
        return false;
    }

    /// <summary>
    /// <paramref name="node"/> rebuilt with <paramref name="rewrite"/> applied to each of its child bodies (see
    /// <see cref="ChildStatements"/>), in that order, as one <see cref="AstTrampoline"/> call per body; a block is
    /// passed to <paramref name="rewrite"/> itself and any other node is kept as it is. <paramref name="result"/>
    /// gets the rebuilt node.
    /// </summary>
    public static IEnumerable<AstSubcall> WithRewrittenBodiesIt(IVisitableAstNode node,
        Func<BlockNode, AstResult<BlockNode>, IEnumerable<AstSubcall>> rewrite,
        AstResult<IVisitableAstNode> result) {
        switch (node) {
            case BlockNode block: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(rewrite(block, body));
                result.Value = body.Value!;
                yield break;
            }
            case InstructionBoundaryNode boundary: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(rewrite(boundary.Body, body));
                result.Value = boundary with { Body = body.Value! };
                yield break;
            }
            case IfElseNode ifElse: {
                AstResult<BlockNode> whenTrue = new();
                yield return AstTrampoline.Call(rewrite(Sequence(ifElse.TrueCase), whenTrue));
                AstResult<BlockNode> whenFalse = new();
                yield return AstTrampoline.Call(rewrite(Sequence(ifElse.FalseCase), whenFalse));
                result.Value = new IfElseNode(ifElse.Condition, whenTrue.Value!, whenFalse.Value!);
                yield break;
            }
            case WhileNode whileNode: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(rewrite(whileNode.Body, body));
                result.Value = new WhileNode(whileNode.Condition, body.Value!);
                yield break;
            }
            case PreTestLoopNode preTestLoop: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(rewrite(preTestLoop.Body, body));
                result.Value = preTestLoop with { Body = body.Value! };
                yield break;
            }
            case InfiniteLoopNode loop: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(rewrite(loop.Body, body));
                result.Value = loop with { Body = body.Value! };
                yield break;
            }
            case DoWhileNode doWhile: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(rewrite(doWhile.Body, body));
                result.Value = doWhile with { Body = body.Value! };
                yield break;
            }
            case SwitchNode switchNode: {
                List<SwitchSection> sections = new(switchNode.Sections.Count);
                foreach (SwitchSection section in switchNode.Sections) {
                    AstResult<BlockNode> body = new();
                    yield return AstTrampoline.Call(rewrite(section.Body, body));
                    sections.Add(section with { Body = body.Value! });
                }
                AstResult<BlockNode> defaultBody = new();
                yield return AstTrampoline.Call(rewrite(switchNode.Default, defaultBody));
                result.Value = switchNode with { Sections = sections, Default = defaultBody.Value! };
                yield break;
            }
            case CpuFaultTryCatchNode tryCatch: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(rewrite(tryCatch.Body, body));
                result.Value = tryCatch with { Body = body.Value! };
                yield break;
            }
            default:
                result.Value = node;
                yield break;
        }
    }

    /// <summary>True when <paramref name="node"/> reads only registers, flags and other CPU state: it can move out of a boundary.</summary>
    public static bool ReadsOnlyMachineState(ValueNode node) => node switch {
        CpuFlagNode or RegisterNode or ConstantNode or InstructionFieldNode
            or MethodScopedVariableReferenceNode => true,
        UnaryOperationNode or BinaryOperationNode or TypeConversionNode or SegmentedPointerNode
            or ExplicitCastNode { Value: ValueNode } =>
            IrAstChildren.ChildrenOf(node).OfType<ValueNode>().All(ReadsOnlyMachineState),
        _ => false
    };

    /// <summary>A switch section whose body ends with <c>break;</c> exactly when it would otherwise complete normally.</summary>
    public static SwitchSection Section(uint label, IVisitableAstNode body) {
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
    /// The goto target a jump lands on when it enters <paramref name="statement"/>: the anchor of a boundary, the
    /// target of a label, the anchor of the first statement of a loop body; <c>null</c> for any other statement.
    /// </summary>
    public static IGotoTarget? AnchorOf(IVisitableAstNode statement) => statement switch {
        InstructionBoundaryNode boundary => boundary.Anchor,
        LabelNode label => label.Target,
        InfiniteLoopNode loop => FirstAnchor(loop.Body),
        PreTestLoopNode loop => loop.HeaderTarget,
        WhileNode loop => FirstAnchor(loop.Body),
        DoWhileNode doWhile => FirstAnchor(doWhile.Body),
        _ => null
    };

    private static IGotoTarget? FirstAnchor(BlockNode body) {
        if (body.Statements.Count == 0) {
            return null;
        }
        return AnchorOf(body.Statements[0]);
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
