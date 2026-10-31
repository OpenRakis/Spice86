namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;
using System.Linq;

using static Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast.CodeGenerationAst;

/// <summary>
/// Lowers IR blocks into the <see cref="LoweredBlock{TBlock}"/>s the structurizer consumes: the statements of a
/// block are grouped into one <see cref="InstructionBoundaryNode"/> per consecutive run of the same origin and the
/// terminator is appended to the last boundary as explicit <c>goto</c>s.
/// </summary>
internal static class IrBlockLowering {
    /// <summary>
    /// The lowered form of <paramref name="blocks"/>, in the given order.
    /// </summary>
    /// <param name="method">The method the blocks belong to, for its entries and back edges.</param>
    /// <param name="blocks">The blocks to lower, in layout order.</param>
    public static IReadOnlyList<LoweredBlock<IrBlock>> Lower(IrMethod method, IReadOnlyList<IrBlock> blocks) {
        BlockGraph<IrBlock> graph = method.ToStructuringGraph();
        DominatorTree<IrBlock> dominators = DominatorTreeBuilder.BuildFromEntries(graph);
        return Lower(method, blocks, graph, dominators, LoopsOf(graph, dominators));
    }

    /// <summary>The lowered form of <paramref name="blocks"/>, reusing the structuring analysis the
    /// caller already built for the method (structuring graph, dominators, natural loops).</summary>
    public static IReadOnlyList<LoweredBlock<IrBlock>> Lower(IrMethod method,
        IReadOnlyList<IrBlock> blocks, BlockGraph<IrBlock> graph, DominatorTree<IrBlock> dominators,
        NaturalLoopForest<IrBlock> loops) {
        HashSet<IrBlock> entryBlocks = [.. method.Entries.Select(entry => entry.Block)];
        List<IrEdge> backEdges = blocks.SelectMany(block => block.Terminator.Edges
            .Where(edge => dominators.Dominates(edge.Target, block))).ToList();
        HashSet<IrBlock> inlinedExits = [.. blocks
            .Where(block => block.Terminator is IrBranch or IrSwitch)
            .SelectMany(block => block.Terminator.Edges
                .Where(edge => InlinesExit(graph, block, edge) && !ExitsLoop(loops, block, edge.Target))
                .Select(edge => edge.Target))];
        HashSet<IrBlock> insideOnly = method.BlocksEnteredOnlyThroughInsideEdges();
        HashSet<IrBlock> needsCheckBlocks = [.. backEdges
            .Where(edge => !edge.InsideInstruction)
            .Select(edge => edge.Target)];
        List<LoweredBlock<IrBlock>> lowered = [];
        foreach (IrBlock block in blocks) {
            if (inlinedExits.Contains(block)) {
                continue;
            }
            lowered.Add(new LoweredBlock<IrBlock>(block, block, HasEntryCheck(block), entryBlocks.Contains(block),
                needsCheckBlocks.Contains(block), insideOnly.Contains(block), BodyOf(block, graph)));
        }
        return lowered;
    }

    /// <summary>The method's natural loops on the structuring graph; empty for a single-block method.</summary>
    private static NaturalLoopForest<IrBlock> LoopsOf(BlockGraph<IrBlock> graph,
        DominatorTree<IrBlock> dominators) {
        DepthFirstOrdering<IrBlock> traversal = new([.. graph.EntryBlocks, .. graph.Blocks],
            graph.GetSuccessors);
        return NaturalLoopForest<IrBlock>.Build(graph, traversal.RetreatingEdges, dominators);
    }

    /// <summary>The edge leaves a natural loop <paramref name="from"/> is part of and lands on a real
    /// instruction block: it keeps its own label so the structurizer can print the jump as a <c>break</c>
    /// instead of an inlined transfer. A statement-less exit arm (an untested failure) is not a loop
    /// continuation and still inlines.</summary>
    private static bool ExitsLoop(NaturalLoopForest<IrBlock> loops, IrBlock from, IrBlock target) =>
        target.StartsInstruction
        && loops.Loops.Any(loop => loop.Contains(from) && !loop.Contains(target));

    /// <summary>The block's first statement is a <see cref="CheckExternalEventsNode"/>.</summary>
    private static bool HasEntryCheck(IrBlock block) =>
        block.Statements.Count > 0 && block.Statements[0].Node is CheckExternalEventsNode;

    /// <summary>
    /// The block body: one boundary per consecutive run of statements with the same origin, the last holding the
    /// terminator's explicit transfers. Only the first boundary carries the block as its anchor, so jumps land on
    /// the block start rather than on an inner statement.
    /// </summary>
    private static BlockNode BodyOf(IrBlock block, BlockGraph<IrBlock> graph) {
        List<InstructionBoundaryNode> boundaries = [];
        List<IVisitableAstNode> current = [];
        ICfgNode? currentOrigin = null;
        foreach (IrStatement statement in block.Statements) {
            if (current.Count > 0 && !statement.Origin.Equals(currentOrigin)) {
                boundaries.Add(new InstructionBoundaryNode(currentOrigin!, "", Sequence(current), null));
                current = [];
            }
            currentOrigin = statement.Origin;
            current.Add(statement.Node);
        }
        if (current.Count > 0) {
            boundaries.Add(new InstructionBoundaryNode(currentOrigin!, "", Sequence(current), null));
        }
        IVisitableAstNode terminator = TerminatorNode(block, graph);
        if (boundaries.Count > 0) {
            InstructionBoundaryNode last = boundaries[^1];
            boundaries[^1] = last with { Body = Sequence([.. last.Body.Statements, terminator]) };
        } else {
            boundaries.Add(new InstructionBoundaryNode(block.Terminator.Origin, "", Sequence(terminator), null));
        }
        boundaries[0] = boundaries[0] with { Anchor = block };
        return Sequence(boundaries);
    }

    /// <summary>The terminator as explicit transfers: a jump, a two-arm branch, a dispatch or an exit.
    /// A branch arm whose target is an exit-only block entered solely from this block prints the
    /// transfer inline, keeping the exit inside the jump's if.</summary>
    private static IVisitableAstNode TerminatorNode(IrBlock block, BlockGraph<IrBlock> graph) =>
        block.Terminator switch {
        IrJump jump => new GotoNode(jump.Edge.Target),
        IrBranch branch => new IfElseNode(branch.Condition,
            new BlockNode(ArmNode(block, branch.WhenTrue, graph)),
            new BlockNode(ArmNode(block, branch.WhenFalse, graph))),
        IrSwitch @switch => new SwitchNode(@switch.Value,
            [.. @switch.Cases.Select(match => new SwitchSection(match.Label,
                new BlockNode(ArmNode(block, match.Target, graph))))],
            new BlockNode(ArmNode(block, @switch.Default, graph))),
        IrExit exit => exit.Transfer,
        _ => throw new NotSupportedException($"IR terminator {block.Terminator.GetType().Name} cannot be lowered.")
    };

    /// <summary>The transfer <paramref name="edge"/>'s target emits inline when the target is an
    /// exit-only block - no statements, an <see cref="IrExit"/> terminator - whose only predecessor is
    /// <paramref name="from"/>; an explicit goto otherwise. A <see cref="MoveIpNextNode"/> transfer is
    /// not inlined: it is a loop-exit fallthrough, and the goto is what the structurizer turns into a
    /// <c>break</c>.</summary>
    private static IVisitableAstNode ArmNode(IrBlock from, IrEdge edge, BlockGraph<IrBlock> graph) =>
        InlinesExit(graph, from, edge) && edge.Target.Terminator is IrExit exit
            ? exit.Transfer
            : new GotoNode(edge.Target);

    private static bool InlinesExit(BlockGraph<IrBlock> graph, IrBlock from, IrEdge edge) =>
        edge.Target is { Statements.Count: 0, Terminator: IrExit { Transfer: not MoveIpNextNode } } target
        && graph.PredecessorsByBlock[target].All(predecessor => ReferenceEquals(predecessor, from));
}
