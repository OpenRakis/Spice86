namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;

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
    public static BlockNode Apply(BlockNode body) => RewriteBlock(body);

    private static BlockNode RewriteBlock(BlockNode block) {
        List<IVisitableAstNode> statements = block.Statements.Select(RewriteStatement).ToList();
        return new BlockNode(statements.ToArray());
    }

    private static IVisitableAstNode RewriteStatement(IVisitableAstNode statement) => statement switch {
        BlockNode nested => RewriteBlock(nested),
        InstructionBoundaryNode boundary => boundary with { Body = RewriteBlock(boundary.Body) },
        IfElseNode ifElse => new IfElseNode(ifElse.Condition, RewriteBlock(Sequence(ifElse.TrueCase)),
            RewriteBlock(Sequence(ifElse.FalseCase))),
        InfiniteLoopNode loop => TryConvert(loop with { Body = RewriteBlock(loop.Body) }),
        DoWhileNode doWhile => doWhile with { Body = RewriteBlock(doWhile.Body) },
        WhileNode whileNode => new WhileNode(whileNode.Condition, RewriteBlock(whileNode.Body)),
        SwitchNode switchNode => switchNode with {
            Sections = switchNode.Sections.Select(section => section with { Body = RewriteBlock(section.Body) }).ToList(),
            Default = RewriteBlock(switchNode.Default)
        },
        CpuFaultTryCatchNode tryCatch => tryCatch with { Body = RewriteBlock(tryCatch.Body) },
        _ => statement
    };

    private static bool OnlyCheckOrComment(IVisitableAstNode statement) =>
        statement is CheckExternalEventsNode or CommentNode;

    private static bool IsPrefixBoundary(IVisitableAstNode statement) =>
        statement is InstructionBoundaryNode boundary && boundary.Body.Statements.All(OnlyCheckOrComment);

    private static bool IsExitTest(IfElseNode node) =>
        Sequence(node.TrueCase).Statements is [BreakNode]
        && IsEmpty(Sequence(node.FalseCase))
        && MethodStructurizer.ReadsOnlyMachineState(node.Condition);

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
