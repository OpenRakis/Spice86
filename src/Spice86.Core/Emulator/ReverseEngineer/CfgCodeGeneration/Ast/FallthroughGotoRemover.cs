namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Removes from a lowered method body every <c>goto</c> that jumps where control goes anyway once the <c>goto</c> is
/// removed. Inside a <c>switch</c> section, a <c>goto</c> to what follows the <c>switch</c> becomes <c>break;</c>,
/// and a conditional whose taken arm became empty is negated. Inside a loop, a <c>goto</c> to what follows
/// the loop becomes <c>break;</c>, and inside a <c>while (true)</c> loop a <c>goto</c> to the start of its body becomes
/// <c>continue;</c>, unless a <c>switch</c> or an inner loop would capture the statement. Inside a <c>while (c)</c>
/// loop built by the structurizer from a tested loop header, a <c>goto</c> to the header becomes <c>continue;</c>
/// the same way. Inside a <c>while (true)</c> or <c>while (c)</c> loop body, control goes from the last statement
/// back to the first one; inside a <c>do</c> loop body, it goes to the condition.
/// Only statement containers are rewritten; every other node is returned as it is.
/// A <c>goto</c> that follows a statement that never completes normally is unreachable and is removed too.
/// </summary>
internal static class FallthroughGotoRemover {
    private static readonly LoopExitTargets NoLoop = new(null, null);

    /// <summary><paramref name="body"/> without its fallthrough <c>goto</c>s; the method body has no follow.</summary>
    public static BlockNode Remove(BlockNode body) {
        AstResult<BlockNode> result = new();
        AstTrampoline.Run(RewriteBlockIt(body, null, NoLoop, result));
        return result.Value!;
    }

    /// <param name="follow">The target control reaches when <paramref name="block"/> completes normally, or <c>null</c> when that is not a goto target.</param>
    /// <param name="loop">Where a <c>break</c> or <c>continue</c> written in <paramref name="block"/> would go.</param>
    private static IEnumerable<AstSubcall> RewriteBlockIt(BlockNode block, IGotoTarget? follow,
        LoopExitTargets loop, AstResult<BlockNode> result) {
        List<IVisitableAstNode> statements = [];
        for (int index = 0; index < block.Statements.Count; index++) {
            IGotoTarget? statementFollow = follow;
            if (index + 1 < block.Statements.Count) {
                statementFollow = ReachedThrough(block.Statements[index + 1]);
            }
            AstResult<IVisitableAstNode> rewritten = new();
            yield return AstTrampoline.Call(RewriteStatementIt(block.Statements[index], statementFollow, loop, rewritten));
            if (rewritten.Value is null) {
                continue;
            }
            bool unreachable = rewritten.Value is GotoNode && statements.Count > 0
                && !CompletesNormallyVisitor.CompletesNormally(statements[^1]);
            if (!unreachable) {
                statements.Add(rewritten.Value);
            }
        }
        result.Value = new BlockNode(statements.ToArray());
    }

    /// <summary>
    /// The target control reaches when it enters <paramref name="statement"/>: the target of a <c>goto</c>, otherwise
    /// <see cref="CodeGenerationAst.AnchorOf"/>.
    /// </summary>
    private static IGotoTarget? ReachedThrough(IVisitableAstNode statement) {
        if (statement is GotoNode jump) {
            return jump.Target;
        }
        return AnchorOf(statement);
    }

    /// <param name="follow">The target control reaches after <paramref name="statement"/>, counting a <c>goto</c> that follows it; the <c>break</c> target of a loop statement.</param>
    /// <param name="result">The rewritten statement, or stays <c>null</c> when it disappears.</param>
    private static IEnumerable<AstSubcall> RewriteStatementIt(IVisitableAstNode statement, IGotoTarget? follow,
        LoopExitTargets loop, AstResult<IVisitableAstNode> result) {
        switch (statement) {
            case GotoNode removed when Equals(removed.Target, follow):
                yield break;
            case GotoNode leaving when Equals(leaving.Target, loop.Break):
                result.Value = new BreakNode();
                yield break;
            case GotoNode restarting when Equals(restarting.Target, loop.Continue):
                result.Value = new ContinueNode();
                yield break;
            case BlockNode block: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(RewriteBlockIt(block, follow, loop, body));
                result.Value = body.Value!;
                yield break;
            }
            case InstructionBoundaryNode boundary: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(RewriteBlockIt(boundary.Body, follow, loop, body));
                result.Value = boundary with { Body = body.Value! };
                yield break;
            }
            case IfElseNode ifElse:
                yield return AstTrampoline.Call(RewriteIfElseIt(ifElse, follow, loop, result));
                yield break;
            case WhileNode whileNode: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(RewriteBlockIt(whileNode.Body, null, NoLoop, body));
                result.Value = new WhileNode(whileNode.Condition, body.Value!);
                yield break;
            }
            case PreTestLoopNode preTestLoop: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(RewriteBlockIt(preTestLoop.Body, preTestLoop.HeaderTarget,
                    new LoopExitTargets(follow, preTestLoop.HeaderTarget), body));
                result.Value = preTestLoop with { Body = body.Value! };
                yield break;
            }
            case InfiniteLoopNode infiniteLoop: {
                IGotoTarget? start = AnchorOf(infiniteLoop);
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(RewriteBlockIt(infiniteLoop.Body, start,
                    new LoopExitTargets(follow, start), body));
                result.Value = infiniteLoop with { Body = body.Value! };
                yield break;
            }
            case DoWhileNode doWhile: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(RewriteBlockIt(doWhile.Body, null, new LoopExitTargets(follow, null), body));
                result.Value = doWhile with { Body = body.Value! };
                yield break;
            }
            case SwitchNode switchNode:
                yield return AstTrampoline.Call(RewriteSwitchIt(switchNode, follow, result));
                yield break;
            case CpuFaultTryCatchNode tryCatch: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(RewriteBlockIt(tryCatch.Body, follow, loop, body));
                result.Value = tryCatch with { Body = body.Value! };
                yield break;
            }
            default:
                result.Value = statement;
                yield break;
        }
    }

    private static IEnumerable<AstSubcall> RewriteIfElseIt(IfElseNode node, IGotoTarget? follow,
        LoopExitTargets loop, AstResult<IVisitableAstNode> result) {
        BlockNode originalTrueArm = Sequence(node.TrueCase);
        BlockNode originalFalseArm = Sequence(node.FalseCase);
        AstResult<BlockNode> trueArm = new();
        yield return AstTrampoline.Call(RewriteBlockIt(originalTrueArm, follow, loop, trueArm));
        AstResult<BlockNode> falseArm = new();
        yield return AstTrampoline.Call(RewriteBlockIt(originalFalseArm, follow, loop, falseArm));
        bool armEmptied = (IsEmpty(trueArm.Value!) && !IsEmpty(originalTrueArm)) || (IsEmpty(falseArm.Value!) && !IsEmpty(originalFalseArm));
        if (armEmptied && IsEmpty(trueArm.Value!) && IsEmpty(falseArm.Value!)) {
            yield break;
        }
        if (armEmptied && IsEmpty(trueArm.Value!)) {
            result.Value = new IfElseNode(NegatedCondition(node.Condition), falseArm.Value!, Empty());
            yield break;
        }
        result.Value = new IfElseNode(node.Condition, trueArm.Value!, falseArm.Value!);
    }

    private static IEnumerable<AstSubcall> RewriteSwitchIt(SwitchNode node, IGotoTarget? follow,
        AstResult<IVisitableAstNode> result) {
        List<SwitchSection> sections = new(node.Sections.Count);
        foreach (SwitchSection section in node.Sections) {
            AstResult<BlockNode> body = new();
            yield return AstTrampoline.Call(RewriteBlockIt(section.Body, null,
                new LoopExitTargets(follow, null), body));
            sections.Add(section with { Body = body.Value! });
        }
        AstResult<BlockNode> defaultBody = new();
        yield return AstTrampoline.Call(RewriteBlockIt(node.Default, null,
            new LoopExitTargets(follow, null), defaultBody));
        result.Value = node with { Sections = sections, Default = defaultBody.Value! };
    }

    /// <summary>Where a <c>break</c> or a <c>continue</c> of the innermost enclosing loop goes; <c>null</c> when the statement cannot be used there.</summary>
    private sealed record LoopExitTargets(IGotoTarget? Break, IGotoTarget? Continue);
}
