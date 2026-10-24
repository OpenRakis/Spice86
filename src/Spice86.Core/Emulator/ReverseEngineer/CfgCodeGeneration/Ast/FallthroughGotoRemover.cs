namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Removes from a lowered method body every <c>goto</c> that jumps where control goes anyway once the <c>goto</c> is
/// removed. A <c>goto</c> that ends a <c>switch</c> section and jumps to what follows the <c>switch</c> becomes
/// <c>break;</c>, and a conditional whose taken arm became empty is negated. Inside a loop, a <c>goto</c> to what follows
/// the loop becomes <c>break;</c>, and inside a <c>while (true)</c> loop a <c>goto</c> to the start of its body becomes
/// <c>continue;</c>, unless a <c>switch</c> or an inner loop would capture the statement. Inside a <c>while (true)</c>
/// loop body, control goes from the last statement back to the first one; inside a <c>do</c> loop body, it goes to the
/// condition. Only statement containers are rewritten; every other node is returned as it is.
/// </summary>
internal static class FallthroughGotoRemover {
    private static readonly LoopExitTargets NoLoop = new(null, null);

    /// <summary><paramref name="body"/> without its fallthrough <c>goto</c>s; the method body has no follow.</summary>
    public static BlockNode Remove(BlockNode body) => RewriteBlock(body, null, NoLoop);

    /// <param name="follow">The node control reaches when <paramref name="block"/> completes normally, or <c>null</c> when that is not the start of a CFG node.</param>
    /// <param name="loop">Where a <c>break</c> or <c>continue</c> written in <paramref name="block"/> would go.</param>
    private static BlockNode RewriteBlock(BlockNode block, ICfgNode? follow, LoopExitTargets loop) {
        List<IVisitableAstNode> statements = [];
        for (int index = 0; index < block.Statements.Count; index++) {
            ICfgNode? statementFollow = follow;
            if (index + 1 < block.Statements.Count) {
                statementFollow = ReachedThrough(block.Statements[index + 1]);
            }
            if (RewriteStatement(block.Statements[index], statementFollow, loop) is IVisitableAstNode rewritten) {
                statements.Add(rewritten);
            }
        }
        return new BlockNode(statements.ToArray());
    }

    /// <summary>
    /// The node control reaches when it enters <paramref name="statement"/>: the target of a <c>goto</c>, otherwise
    /// <see cref="CodeGenerationAst.EntryNodeOf"/>.
    /// </summary>
    private static ICfgNode? ReachedThrough(IVisitableAstNode statement) {
        if (statement is GotoNode jump) {
            return jump.Target;
        }
        return EntryNodeOf(statement);
    }

    /// <param name="follow">The node control reaches after <paramref name="statement"/>, counting a <c>goto</c> that follows it; the <c>break</c> target of a loop statement.</param>
    /// <returns>The rewritten statement, or <c>null</c> when it disappears.</returns>
    private static IVisitableAstNode? RewriteStatement(IVisitableAstNode statement, ICfgNode? follow,
        LoopExitTargets loop) {
        switch (statement) {
            case GotoNode removed when ReferenceEquals(removed.Target, follow):
                return null;
            case GotoNode leaving when ReferenceEquals(leaving.Target, loop.Break):
                return new BreakNode();
            case GotoNode restarting when ReferenceEquals(restarting.Target, loop.Continue):
                return new ContinueNode();
            case BlockNode block:
                return RewriteBlock(block, follow, loop);
            case InstructionBoundaryNode boundary:
                return boundary with { Body = RewriteBlock(boundary.Body, follow, loop) };
            case IfElseNode ifElse:
                return RewriteIfElse(ifElse, follow, loop);
            case WhileNode whileNode:
                return new WhileNode(whileNode.Condition, RewriteBlock(whileNode.Body, null, NoLoop));
            case InfiniteLoopNode infiniteLoop: {
                ICfgNode? start = EntryNodeOf(infiniteLoop);
                return infiniteLoop with { Body = RewriteBlock(infiniteLoop.Body, start, new LoopExitTargets(follow, start)) };
            }
            case DoWhileNode doWhile:
                return doWhile with { Body = RewriteBlock(doWhile.Body, null, new LoopExitTargets(follow, null)) };
            case SwitchNode switchNode:
                return RewriteSwitch(switchNode, follow);
            case CpuFaultTryCatchNode tryCatch:
                return tryCatch with { Body = RewriteBlock(tryCatch.Body, follow, loop) };
            default:
                return statement;
        }
    }

    private static IVisitableAstNode? RewriteIfElse(IfElseNode node, ICfgNode? follow, LoopExitTargets loop) {
        BlockNode originalTrueArm = Sequence(node.TrueCase);
        BlockNode originalFalseArm = Sequence(node.FalseCase);
        BlockNode trueArm = RewriteBlock(originalTrueArm, follow, loop);
        BlockNode falseArm = RewriteBlock(originalFalseArm, follow, loop);
        bool armEmptied = (IsEmpty(trueArm) && !IsEmpty(originalTrueArm)) || (IsEmpty(falseArm) && !IsEmpty(originalFalseArm));
        if (armEmptied && IsEmpty(trueArm) && IsEmpty(falseArm)) {
            return null;
        }
        if (armEmptied && IsEmpty(trueArm)) {
            return new IfElseNode(NegatedCondition(node.Condition), falseArm, Empty());
        }
        return new IfElseNode(node.Condition, trueArm, falseArm);
    }

    private static SwitchNode RewriteSwitch(SwitchNode node, ICfgNode? follow) {
        List<SwitchSection> sections = node.Sections
            .Select(section => section with { Body = RewriteSection(section.Body, follow) })
            .ToList();
        return node with { Sections = sections, Default = RewriteSection(node.Default, follow) };
    }

    private static BlockNode RewriteSection(BlockNode body, ICfgNode? switchFollow) {
        BlockNode rewritten = RewriteBlock(body, null, NoLoop);
        if (rewritten.Statements is [.., GotoNode last] && ReferenceEquals(last.Target, switchFollow)) {
            return Sequence(rewritten.Statements.Take(rewritten.Statements.Count - 1).Append(new BreakNode()));
        }
        return rewritten;
    }

    /// <summary>Where a <c>break</c> or a <c>continue</c> of the innermost enclosing loop goes; <c>null</c> when the statement cannot be used there.</summary>
    private sealed record LoopExitTargets(ICfgNode? Break, ICfgNode? Continue);
}
