namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Collections.Immutable;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Puts the label of every <c>goto</c> target before the outermost statement that starts at that node, so a loop
/// whose body starts at the target gets the label before the loop and stays reachable from outside it. Only the
/// containers that can hold an instruction boundary are searched.
/// </summary>
internal static class LabelPlacer {
    /// <summary><paramref name="body"/> with one <see cref="LabelNode"/> per node of <paramref name="targets"/>.</summary>
    /// <exception cref="InvalidOperationException">No statement starts at a target (a generator bug).</exception>
    public static BlockNode Place(BlockNode body, ImmutableHashSet<ICfgNode> targets) {
        HashSet<ICfgNode> placed = [];
        BlockNode labeled = PlaceInBlock(body, targets, placed);
        foreach (ICfgNode target in targets) {
            if (!placed.Contains(target)) {
                throw new InvalidOperationException($"No statement starts at goto target {target.Address}, so its label cannot be placed.");
            }
        }
        return labeled;
    }

    private static BlockNode PlaceInBlock(BlockNode block, ImmutableHashSet<ICfgNode> targets, HashSet<ICfgNode> placed) {
        List<IVisitableAstNode> statements = [];
        foreach (IVisitableAstNode statement in block.Statements) {
            if (EntryNodeOf(statement) is ICfgNode entry && targets.Contains(entry) && placed.Add(entry)) {
                statements.Add(new LabelNode(entry));
            }
            statements.Add(PlaceInStatement(statement, targets, placed));
        }
        return new BlockNode(statements.ToArray());
    }

    private static IVisitableAstNode PlaceInStatement(IVisitableAstNode statement, ImmutableHashSet<ICfgNode> targets,
        HashSet<ICfgNode> placed) {
        switch (statement) {
            case BlockNode block:
                return PlaceInBlock(block, targets, placed);
            case IfElseNode ifElse:
                return new IfElseNode(ifElse.Condition,
                    PlaceInBlock(Sequence(ifElse.TrueCase), targets, placed),
                    PlaceInBlock(Sequence(ifElse.FalseCase), targets, placed));
            case InfiniteLoopNode loop:
                return loop with { Body = PlaceInBlock(loop.Body, targets, placed) };
            case DoWhileNode doWhile:
                return doWhile with { Body = PlaceInBlock(doWhile.Body, targets, placed) };
            case WhileNode whileNode:
                return new WhileNode(whileNode.Condition, PlaceInBlock(whileNode.Body, targets, placed));
            case SwitchNode switchNode:
                return switchNode with {
                    Sections = switchNode.Sections
                        .Select(section => section with { Body = PlaceInBlock(section.Body, targets, placed) })
                        .ToList(),
                    Default = PlaceInBlock(switchNode.Default, targets, placed)
                };
            default:
                return statement;
        }
    }
}
