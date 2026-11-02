namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Combines two tests that decide one after the other into a single short-circuit condition. A nested
/// <c>if (c1) { if (c2) { T } }</c> becomes <c>if (c1 &amp;&amp; c2) { T }</c>, and two movable <c>if</c>s in one
/// statement list whose true arms are the same exit become <c>if (c1 || c2) { S }</c>. Evaluation order and count are
/// unchanged: <c>&amp;&amp;</c> / <c>||</c> evaluate the second condition only when the first one does not decide,
/// exactly when the second <c>if</c> ran before; only empty boundaries move, and a boundary a <c>goto</c> or a
/// method entry can reach is never moved or emptied, because landing there would run the combined condition.
/// </summary>
internal static class ConditionCombiner {
    /// <summary><paramref name="body"/> with both combining rules applied; <c>&amp;&amp;</c> runs innermost first, <c>||</c> until no pair is left.</summary>
    /// <param name="jumpTargets">The targets a <c>goto</c> in <paramref name="body"/> or a method entry can land on.</param>
    public static BlockNode Apply(BlockNode body, IReadOnlySet<IGotoTarget> jumpTargets) {
        AstResult<BlockNode> result = new();
        AstTrampoline.Run(RewriteListIt(body, false, jumpTargets, result));
        return result.Value!;
    }

    private static IEnumerable<AstSubcall> RewriteListIt(BlockNode block, bool boundaryBody,
        IReadOnlySet<IGotoTarget> jumpTargets, AstResult<BlockNode> result) {
        List<IVisitableAstNode> statements = new(block.Statements.Count);
        bool changed = false;
        foreach (IVisitableAstNode statement in block.Statements) {
            AstResult<IVisitableAstNode> rewritten = new();
            yield return AstTrampoline.Call(RewriteStatementIt(statement, jumpTargets, rewritten));
            changed |= !ReferenceEquals(rewritten.Value, statement);
            statements.Add(rewritten.Value!);
        }
        while (true) {
            bool combined = CombineSequential(statements, jumpTargets);
            if (!boundaryBody) {
                combined |= CombineNested(statements, jumpTargets);
            }
            if (!combined) {
                break;
            }
            changed = true;
        }
        result.Value = changed ? new BlockNode(statements.ToArray()) : block;
    }

    private static IEnumerable<AstSubcall> RewriteStatementIt(IVisitableAstNode statement,
        IReadOnlySet<IGotoTarget> jumpTargets, AstResult<IVisitableAstNode> result) {
        if (statement is InstructionBoundaryNode boundary) {
            AstResult<BlockNode> body = new();
            yield return AstTrampoline.Call(RewriteListIt(boundary.Body, true, jumpTargets, body));
            result.Value = boundary with { Body = body.Value! };
            yield break;
        }
        yield return AstTrampoline.Call(WithRewrittenBodiesIt(statement,
            (child, childResult) => RewriteListIt(child, false, jumpTargets, childResult), result));
    }

    /// <summary>Whether any of <paramref name="boundaries"/> is a jump target.</summary>
    private static bool IsJumpTargeted(IEnumerable<InstructionBoundaryNode> boundaries,
        IReadOnlySet<IGotoTarget> jumpTargets) => boundaries.Any(boundary
        => boundary.Anchor is not null && jumpTargets.Contains(boundary.Anchor));

    /// <summary>A boundary is empty when its body holds only comments: it prints nothing but its asm comment.</summary>
    private static bool IsEmptyBoundary(IVisitableAstNode statement) =>
        statement is InstructionBoundaryNode boundary && boundary.Body.Statements.All(CommentLike);

    private static bool CommentLike(IVisitableAstNode statement) =>
        statement is CommentNode or InstructionStartNode;

    /// <summary>An <see cref="IfElseNode"/> with an empty false arm, or <c>null</c>.</summary>
    private static IfElseNode? BareIf(IVisitableAstNode statement) =>
        statement is IfElseNode ifElse && IsEmpty(Sequence(ifElse.FalseCase)) ? ifElse : null;

    /// <summary>The <c>if</c> that is the last statement of a boundary's body when its condition reads only machine
    /// state, so it can move out of the boundary; <c>null</c> otherwise.</summary>
    private static IfElseNode? MovableIfInBoundary(InstructionBoundaryNode boundary) {
        IReadOnlyList<IVisitableAstNode> statements = boundary.Body.Statements;
        if (statements.Count > 0 && BareIf(statements[^1]) is { } ifElse
            && ReadsOnlyMachineState(ifElse.Condition)) {
            return ifElse;
        }
        return null;
    }

    /// <summary>The position of a movable <c>if</c>: <see cref="Boundary"/> is the boundary holding it as its last
    /// statement, or <c>null</c> when the <c>if</c> is itself a statement of the list.</summary>
    private record MovableIf(IfElseNode IfElse, InstructionBoundaryNode? Boundary);

    /// <summary>The first <c>if</c> of a <c>||</c> pair: list-level, or the last statement of a boundary that keeps
    /// its other statements.</summary>
    private static MovableIf? FirstIfOf(IVisitableAstNode statement) {
        if (BareIf(statement) is { } atListLevel) {
            return new MovableIf(atListLevel, null);
        }
        if (statement is InstructionBoundaryNode boundary && MovableIfInBoundary(boundary) is { } inBoundary) {
            return new MovableIf(inBoundary, boundary);
        }
        return null;
    }

    /// <summary>The second <c>if</c> of a <c>||</c> pair: list-level, or the only statement of its boundary, which is
    /// left empty; a boundary statement before it would be a statement between the two <c>if</c>s.</summary>
    private static MovableIf? SecondIfOf(IVisitableAstNode statement) {
        if (BareIf(statement) is { } atListLevel) {
            return new MovableIf(atListLevel, null);
        }
        if (statement is InstructionBoundaryNode boundary && MovableIfInBoundary(boundary) is { } inBoundary
            && boundary.Body.Statements.Count(s => !CommentLike(s)) == 1) {
            return new MovableIf(inBoundary, boundary);
        }
        return null;
    }

    /// <summary>Both true arms are the same exit: a <c>goto</c> to the same target, a <c>break</c>, a <c>continue</c>,
    /// or the same statements by reference when they never complete normally.</summary>
    private static bool SameExit(BlockNode first, BlockNode second) {
        IReadOnlyList<IVisitableAstNode> a = first.Statements;
        IReadOnlyList<IVisitableAstNode> b = second.Statements;
        if (a.Count == 1 && b.Count == 1) {
            if (a[0] is GotoNode go1 && b[0] is GotoNode go2) {
                return Equals(go1.Target, go2.Target);
            }
            if (a[0] is BreakNode && b[0] is BreakNode) {
                return true;
            }
            if (a[0] is ContinueNode && b[0] is ContinueNode) {
                return true;
            }
        }
        return a.Count == b.Count
            && a.Zip(b).All(pair => ReferenceEquals(pair.First, pair.Second))
            && !CompletesNormallyVisitor.CompletesNormally(first);
    }

    /// <summary><paramref name="boundary"/> without <paramref name="ifElse"/>: its other statements stay.</summary>
    private static InstructionBoundaryNode Without(InstructionBoundaryNode boundary, IfElseNode ifElse) =>
        boundary with {
            Body = new BlockNode(boundary.Body.Statements.Where(s => !ReferenceEquals(s, ifElse)).ToArray())
        };

    /// <summary>One <c>||</c> rewrite, or <c>false</c> when no pair is left: the combined <c>if (c1 || c2) { S }</c>
    /// takes the second <c>if</c>'s place; refused when a boundary between the two <c>if</c>s is a jump target,
    /// because landing there would evaluate <c>c1</c> the original code never ran.</summary>
    private static bool CombineSequential(List<IVisitableAstNode> statements, IReadOnlySet<IGotoTarget> jumpTargets) {
        for (int i = 0; i < statements.Count; i++) {
            if (FirstIfOf(statements[i]) is not { } first) {
                continue;
            }
            int j = i + 1;
            List<InstructionBoundaryNode> between = [];
            while (j < statements.Count && IsEmptyBoundary(statements[j])) {
                between.Add((InstructionBoundaryNode)statements[j]);
                j++;
            }
            if (j >= statements.Count || SecondIfOf(statements[j]) is not { } second
                || !SameExit(Sequence(first.IfElse.TrueCase), Sequence(second.IfElse.TrueCase))) {
                continue;
            }
            if (second.Boundary is not null) {
                between.Add(second.Boundary);
            }
            if (IsJumpTargeted(between, jumpTargets)) {
                continue;
            }
            IVisitableAstNode secondElement = statements[j];
            if (first.Boundary is null) {
                statements.RemoveAt(i);
            } else {
                statements[i] = Without(first.Boundary, first.IfElse);
            }
            int secondIndex = statements.FindIndex(s => ReferenceEquals(s, secondElement));
            if (second.Boundary is null) {
                statements[secondIndex] = new IfElseNode(
                    new BinaryOperationNode(DataType.BOOL, first.IfElse.Condition, BinaryOperation.LOGICAL_OR,
                        second.IfElse.Condition),
                    first.IfElse.TrueCase, new BlockNode());
            } else {
                statements[secondIndex] = Without(second.Boundary, second.IfElse);
                statements.Insert(secondIndex + 1, new IfElseNode(
                    new BinaryOperationNode(DataType.BOOL, first.IfElse.Condition, BinaryOperation.LOGICAL_OR,
                        second.IfElse.Condition),
                    first.IfElse.TrueCase, new BlockNode()));
            }
            return true;
        }
        return false;
    }

    /// <summary>One <c>&amp;&amp;</c> rewrite per nested pair: <c>if (c1)</c> whose arm is only empty boundaries and a
    /// movable <c>if (c2)</c> becomes <c>if (c1 &amp;&amp; c2)</c> with the boundaries placed before it; refused when a
    /// boundary it moves is a jump target, because landing there would evaluate <c>c1</c> the original code
    /// never ran.</summary>
    private static bool CombineNested(List<IVisitableAstNode> statements, IReadOnlySet<IGotoTarget> jumpTargets) {
        bool changed = false;
        for (int i = 0; i < statements.Count; i++) {
            changed |= CombineNestedAt(statements, i, jumpTargets);
        }
        return changed;
    }

    private static bool CombineNestedAt(List<IVisitableAstNode> statements, int index,
        IReadOnlySet<IGotoTarget> jumpTargets) {
        if (BareIf(statements[index]) is not { } outer || Sequence(outer.TrueCase) is not { Statements.Count: > 0 } arm) {
            return false;
        }
        int last = arm.Statements.Count - 1;
        List<InstructionBoundaryNode> moved = [];
        for (int i = 0; i < last; i++) {
            if (!IsEmptyBoundary(arm.Statements[i])) {
                return false;
            }
            moved.Add((InstructionBoundaryNode)arm.Statements[i]);
        }
        IfElseNode inner;
        if (BareIf(arm.Statements[last]) is { } statementLevel) {
            inner = statementLevel;
        } else if (arm.Statements[last] is InstructionBoundaryNode lastBoundary
            && MovableIfInBoundary(lastBoundary) is { } inBoundary
            && lastBoundary.Body.Statements.Count(s => !CommentLike(s)) == 1) {
            inner = inBoundary;
            moved.Add(Without(lastBoundary, inBoundary));
        } else {
            return false;
        }
        if (IsJumpTargeted(moved, jumpTargets)) {
            return false;
        }
        IfElseNode combined = new(
            new BinaryOperationNode(DataType.BOOL, outer.Condition, BinaryOperation.LOGICAL_AND, inner.Condition),
            inner.TrueCase, new BlockNode());
        statements.RemoveAt(index);
        statements.InsertRange(index, [.. moved, combined]);
        return true;
    }
}
