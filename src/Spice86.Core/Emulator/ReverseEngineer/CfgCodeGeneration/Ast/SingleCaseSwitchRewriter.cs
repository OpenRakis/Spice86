namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

using System.Collections.Generic;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Turns a <see cref="SwitchNode"/> with exactly one case section into an <c>if</c>: the same comparison
/// and the same transfers, with the switch value evaluated once in the condition.
/// One trailing <c>break;</c> is stripped from the case body and from the default body first, looking
/// through a trailing transparent container (a plain block or an instruction boundary) to find it; the
/// rewrite is refused when a <c>break;</c> that still binds to the switch remains in either body (a
/// <c>break</c> nested in an inner loop or an inner <c>switch</c> does not bind to it). When the stripped
/// default body does not complete normally the result is <c>if (value != label) { default body }</c>
/// followed by the case body statements; otherwise it is <c>if (value == label) { case body } else
/// { default body }</c>.
/// </summary>
internal static class SingleCaseSwitchRewriter {
    /// <summary><paramref name="body"/> with every qualifying single-case <c>switch</c> rewritten to an <c>if</c>.</summary>
    public static BlockNode Apply(BlockNode body) {
        AstResult<BlockNode> result = new();
        AstTrampoline.Run(RewriteBlockIt(body, result));
        return result.Value ?? throw new InvalidOperationException(
            "The block rewrite trampoline produced no result.");
    }

    private static IEnumerable<AstSubcall> RewriteBlockIt(BlockNode block, AstResult<BlockNode> result) {
        List<IVisitableAstNode> statements = new(block.Statements.Count);
        foreach (IVisitableAstNode statement in block.Statements) {
            AstResult<IVisitableAstNode> rewritten = new();
            yield return AstTrampoline.Call(WithRewrittenBodiesIt(statement, RewriteBlockIt, rewritten));
            if (rewritten.Value is SwitchNode switchNode && Convert(switchNode) is { } replacement) {
                statements.AddRange(replacement);
            } else {
                statements.Add(rewritten.Value ?? throw new InvalidOperationException(
                    "The statement body rewrite produced no result."));
            }
        }
        result.Value = new BlockNode(statements.ToArray());
    }

    /// <summary>The statements <paramref name="node"/> rewrites to, or <see langword="null"/> when it is
    /// not a single-case switch whose bodies are free of <c>break;</c>s that bind to it.</summary>
    private static List<IVisitableAstNode>? Convert(SwitchNode node) {
        if (node.Sections is not [SwitchSection section]) {
            return null;
        }
        BlockNode caseBody = WithoutTrailingBreak(section.Body);
        BlockNode defaultBody = WithoutTrailingBreak(node.Default);
        if (BindsToSwitch(caseBody) || BindsToSwitch(defaultBody)) {
            return null;
        }
        ConstantNode label = new(node.Value.DataType, section.Label);
        BinaryOperationNode notEqual = new(DataType.BOOL, node.Value, BinaryOperation.NOT_EQUAL, label);
        if (IsEmpty(caseBody) || !CompletesNormallyVisitor.CompletesNormally(defaultBody)) {
            return [.. Sequence(new IfElseNode(notEqual, defaultBody, Empty()), caseBody).Statements];
        }
        BinaryOperationNode equal = new(DataType.BOOL, node.Value, BinaryOperation.EQUAL, label);
        return [new IfElseNode(equal, caseBody, defaultBody)];
    }

    /// <summary><paramref name="body"/> without the <c>break;</c> that ends it, looking through a trailing
    /// transparent container (a plain block or an instruction boundary) to find it.</summary>
    private static BlockNode WithoutTrailingBreak(BlockNode body) {
        IReadOnlyList<IVisitableAstNode> statements = body.Statements;
        if (statements.Count == 0) {
            return body;
        }
        switch (statements[^1]) {
            case BreakNode:
                return new BlockNode(statements.Take(statements.Count - 1).ToArray());
            case InstructionBoundaryNode boundary: {
                BlockNode inner = WithoutTrailingBreak(boundary.Body);
                if (ReferenceEquals(inner, boundary.Body)) {
                    return body;
                }
                return new BlockNode([.. statements.Take(statements.Count - 1),
                    boundary with { Body = inner }]);
            }
            case BlockNode nested: {
                BlockNode inner = WithoutTrailingBreak(nested);
                if (ReferenceEquals(inner, nested)) {
                    return body;
                }
                return new BlockNode([.. statements.Take(statements.Count - 1), inner]);
            }
            default:
                return body;
        }
    }

    /// <summary>Whether <paramref name="node"/> holds a <c>break;</c> that binds to the switch its body is a
    /// section of: one not nested inside an inner loop or an inner <c>switch</c>.</summary>
    private static bool BindsToSwitch(IVisitableAstNode node) {
        Stack<IVisitableAstNode> pending = new();
        pending.Push(node);
        while (pending.Count > 0) {
            IVisitableAstNode current = pending.Pop();
            switch (current) {
                case BreakNode:
                    return true;
                case SwitchNode or WhileNode or PreTestLoopNode or InfiniteLoopNode or DoWhileNode:
                    continue;
                default:
                    foreach (IVisitableAstNode child in ChildStatements(current)) {
                        pending.Push(child);
                    }
                    break;
            }
        }
        return false;
    }
}
