namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

using System.Collections.Immutable;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Puts the label of every <c>goto</c> target before the outermost statement that starts at that target, so a loop
/// whose body starts at the target gets the label before the loop and stays reachable from outside it. Only the
/// containers that can hold an instruction boundary are searched. A <see cref="LabelNode"/> already in the tree
/// (lowered pipelines can place labels themselves) satisfies its target.
/// </summary>
internal static class LabelPlacer {
    /// <summary><paramref name="body"/> with one <see cref="LabelNode"/> per target of <paramref name="targets"/>.</summary>
    /// <exception cref="InvalidOperationException">No statement starts at a target (a generator bug).</exception>
    public static BlockNode Place(BlockNode body, ImmutableHashSet<IGotoTarget> targets) {
        HashSet<IGotoTarget> placed = [];
        AstResult<BlockNode> labeled = new();
        AstTrampoline.Run(PlaceInBlockIt(body, targets, placed, labeled));
        foreach (IGotoTarget target in targets) {
            if (!placed.Contains(target)) {
                throw new InvalidOperationException($"No statement starts at goto target {target}, so its label cannot be placed.");
            }
        }
        return labeled.Value!;
    }

    private static IEnumerable<AstSubcall> PlaceInBlockIt(BlockNode block, ImmutableHashSet<IGotoTarget> targets,
        HashSet<IGotoTarget> placed, AstResult<BlockNode> result) {
        List<IVisitableAstNode> statements = [];
        foreach (IVisitableAstNode statement in block.Statements) {
            if (statement is LabelNode existing) {
                placed.Add(existing.Target);
                statements.Add(existing);
                continue;
            }
            if (AnchorOf(statement) is IGotoTarget entry && targets.Contains(entry) && placed.Add(entry)) {
                statements.Add(new LabelNode(entry));
            }
            AstResult<IVisitableAstNode> rewritten = new();
            yield return AstTrampoline.Call(PlaceInStatementIt(statement, targets, placed, rewritten));
            statements.Add(rewritten.Value!);
        }
        result.Value = new BlockNode(statements.ToArray());
    }

    private static IEnumerable<AstSubcall> PlaceInStatementIt(IVisitableAstNode statement,
        ImmutableHashSet<IGotoTarget> targets, HashSet<IGotoTarget> placed,
        AstResult<IVisitableAstNode> result) {
        switch (statement) {
            case BlockNode block: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(PlaceInBlockIt(block, targets, placed, body));
                result.Value = body.Value!;
                yield break;
            }
            case IfElseNode ifElse: {
                AstResult<BlockNode> whenTrue = new();
                yield return AstTrampoline.Call(PlaceInBlockIt(Sequence(ifElse.TrueCase), targets, placed, whenTrue));
                AstResult<BlockNode> whenFalse = new();
                yield return AstTrampoline.Call(PlaceInBlockIt(Sequence(ifElse.FalseCase), targets, placed, whenFalse));
                result.Value = new IfElseNode(ifElse.Condition, whenTrue.Value!, whenFalse.Value!);
                yield break;
            }
            case InfiniteLoopNode loop: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(PlaceInBlockIt(loop.Body, targets, placed, body));
                result.Value = loop with { Body = body.Value! };
                yield break;
            }
            case DoWhileNode doWhile: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(PlaceInBlockIt(doWhile.Body, targets, placed, body));
                result.Value = doWhile with { Body = body.Value! };
                yield break;
            }
            case WhileNode whileNode: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(PlaceInBlockIt(whileNode.Body, targets, placed, body));
                result.Value = new WhileNode(whileNode.Condition, body.Value!);
                yield break;
            }
            case PreTestLoopNode preTestLoop: {
                AstResult<BlockNode> body = new();
                yield return AstTrampoline.Call(PlaceInBlockIt(preTestLoop.Body, targets, placed, body));
                result.Value = preTestLoop with { Body = body.Value! };
                yield break;
            }
            case SwitchNode switchNode: {
                List<SwitchSection> sections = new(switchNode.Sections.Count);
                foreach (SwitchSection section in switchNode.Sections) {
                    AstResult<BlockNode> body = new();
                    yield return AstTrampoline.Call(PlaceInBlockIt(section.Body, targets, placed, body));
                    sections.Add(section with { Body = body.Value! });
                }
                AstResult<BlockNode> defaultBody = new();
                yield return AstTrampoline.Call(PlaceInBlockIt(switchNode.Default, targets, placed, defaultBody));
                result.Value = switchNode with { Sections = sections, Default = defaultBody.Value! };
                yield break;
            }
            default:
                result.Value = statement;
                yield break;
        }
    }
}
