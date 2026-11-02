namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Turns a <c>while (true)</c> whose body starts with event checks and comments and then leaves on a machine-state
/// condition into a <c>while (!c)</c> with the exit test removed: the statements before the test only print comments
/// and event checks, so testing the condition first runs them one check later when the loop is left. Any other
/// statement before the test keeps the loop as it is.
/// </summary>
internal static class WhileConditionLoops {
    /// <summary><paramref name="body"/> with every qualifying <c>while (true)</c> rewritten to <c>while (!c)</c>.</summary>
    public static BlockNode Apply(BlockNode body) {
        AstResult<BlockNode> result = new();
        AstTrampoline.Run(RewriteBlockIt(body, result));
        return result.Value!;
    }

    private static IEnumerable<AstSubcall> RewriteBlockIt(BlockNode block, AstResult<BlockNode> result) {
        List<IVisitableAstNode> statements = new(block.Statements.Count);
        foreach (IVisitableAstNode statement in block.Statements) {
            AstResult<IVisitableAstNode> rewritten = new();
            yield return AstTrampoline.Call(RewriteStatementIt(statement, rewritten));
            statements.Add(rewritten.Value!);
        }
        result.Value = new BlockNode(statements.ToArray());
    }

    private static IEnumerable<AstSubcall> RewriteStatementIt(IVisitableAstNode statement,
        AstResult<IVisitableAstNode> result) {
        if (statement is InfiniteLoopNode loop) {
            AstResult<BlockNode> body = new();
            yield return AstTrampoline.Call(RewriteBlockIt(loop.Body, body));
            result.Value = TryConvert(loop with { Body = body.Value! });
            yield break;
        }
        yield return AstTrampoline.Call(WithRewrittenBodiesIt(statement, RewriteBlockIt, result));
    }

    private static bool OnlyCheckOrComment(IVisitableAstNode statement) =>
        statement is CheckExternalEventsNode or CommentNode;

    private static bool IsPrefixBoundary(IVisitableAstNode statement) =>
        statement is InstructionBoundaryNode boundary && boundary.Body.Statements.All(OnlyCheckOrComment);

    private static bool IsExitTest(IfElseNode node) =>
        StatementsOf(node.TrueCase) is [BreakNode]
        && StatementsOf(node.FalseCase).Count == 0
        && ReadsOnlyMachineState(node.Condition);

    private static IVisitableAstNode TryConvert(InfiniteLoopNode loop) {
        List<IVisitableAstNode> statements = [.. loop.Body.Statements];
        int testIndex = statements.FindIndex(statement => !IsPrefixBoundary(statement));
        if (testIndex < 0) {
            return loop;
        }
        if (statements[testIndex] is IfElseNode top && IsExitTest(top)) {
            statements.RemoveAt(testIndex);
            return new WhileNode(NegatedCondition(top.Condition), new BlockNode(statements.ToArray()));
        }
        if (statements[testIndex] is InstructionBoundaryNode { Body.Statements: [.., IfElseNode held] } boundary
            && boundary.Body.Statements.Take(boundary.Body.Statements.Count - 1).All(OnlyCheckOrComment)
            && IsExitTest(held)) {
            statements[testIndex] = boundary with {
                Body = new BlockNode(boundary.Body.Statements.Take(boundary.Body.Statements.Count - 1).ToArray())
            };
            return new WhileNode(NegatedCondition(held.Condition), new BlockNode(statements.ToArray()));
        }
        return loop;
    }
}
