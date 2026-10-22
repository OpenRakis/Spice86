namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Liveness;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Linq;

/// <summary>
/// Status-flag liveness of one method: before and after every node, the status flags whose current value can
/// still be read before being overwritten.
/// </summary>
internal sealed class FlagLiveness {
    private readonly IReadOnlyDictionary<ICfgNode, StatusFlags> _liveBefore;
    private readonly IReadOnlyDictionary<ICfgNode, StatusFlags> _liveAfter;

    private FlagLiveness(IReadOnlyDictionary<ICfgNode, StatusFlags> liveBefore, IReadOnlyDictionary<ICfgNode, StatusFlags> liveAfter) {
        _liveBefore = liveBefore;
        _liveAfter = liveAfter;
    }

    /// <summary>The flag liveness of <paramref name="method"/>, computing each node's facts from <paramref name="context"/>.</summary>
    public static FlagLiveness Compute(MethodPlan method, CfgGeneratorContext context) =>
        Compute(method, context, node => node.ExecutionAst);

    /// <summary>The flag liveness of <paramref name="method"/> when each node executes the AST <paramref name="executionAstOf"/> gives for it.</summary>
    public static FlagLiveness Compute(MethodPlan method, CfgGeneratorContext context,
        Func<ICfgNode, IVisitableAstNode> executionAstOf) {
        HashSet<ICfgNode> methodNodes = method.BlockGraph.Blocks.SelectMany(block => block.Instructions).ToHashSet();
        InstructionFlagFactsBuilder factsBuilder = new(context);
        return Compute(method.BlockGraph, method.BlockTraversal,
            node => factsBuilder.Build(node, executionAstOf(node), methodNodes));
    }

    /// <summary>The flag liveness of the blocks of <paramref name="graph"/>, with per-node facts from <paramref name="factsOf"/>.</summary>
    public static FlagLiveness Compute(PartitionBlockGraph graph, DepthFirstOrdering<CfgBlock> traversal,
        Func<ICfgNode, InstructionFlagFacts> factsOf) {
        Dictionary<ICfgNode, InstructionFlagFacts> facts = new();
        foreach (CfgBlock block in graph.Blocks) {
            foreach (ICfgNode node in block.Instructions) {
                facts[node] = factsOf(node);
            }
        }
        BackwardDataflowResult<StatusFlags> blocks = BackwardDataflowSolver.Solve(graph, traversal,
            (block, liveOut) => BlockLiveIn(block, liveOut, facts), (first, second) => first | second, StatusFlags.None);
        Dictionary<ICfgNode, StatusFlags> liveBefore = new();
        Dictionary<ICfgNode, StatusFlags> liveAfter = new();
        foreach (CfgBlock block in graph.Blocks) {
            StatusFlags live = blocks.LiveOut[block];
            for (int index = block.Instructions.Count - 1; index >= 0; index--) {
                ICfgNode node = block.Instructions[index];
                StatusFlags after = NodeLiveAfter(facts[node], live);
                liveAfter[node] = after;
                live = NodeLiveBefore(facts[node], after);
                liveBefore[node] = live;
            }
        }
        return new FlagLiveness(liveBefore, liveAfter);
    }

    /// <summary>The flags live before <paramref name="node"/>.</summary>
    /// <exception cref="KeyNotFoundException">The node is not part of the method.</exception>
    public StatusFlags LiveBefore(ICfgNode node) => _liveBefore[node];

    /// <summary>The flags live after <paramref name="node"/>.</summary>
    /// <exception cref="KeyNotFoundException">The node is not part of the method.</exception>
    public StatusFlags LiveAfter(ICfgNode node) => _liveAfter[node];

    /// <summary>The flags live at the start of <paramref name="block"/>.</summary>
    public StatusFlags LiveIn(CfgBlock block) => _liveBefore[block.Entry];

    /// <summary>The flags live at the end of <paramref name="block"/>.</summary>
    public StatusFlags LiveOut(CfgBlock block) => _liveAfter[block.Terminator];

    private static StatusFlags BlockLiveIn(CfgBlock block, StatusFlags liveOut, IReadOnlyDictionary<ICfgNode, InstructionFlagFacts> facts) {
        StatusFlags live = liveOut;
        for (int index = block.Instructions.Count - 1; index >= 0; index--) {
            InstructionFlagFacts nodeFacts = facts[block.Instructions[index]];
            live = NodeLiveBefore(nodeFacts, NodeLiveAfter(nodeFacts, live));
        }
        return live;
    }

    private static StatusFlags NodeLiveAfter(InstructionFlagFacts facts, StatusFlags liveAtNext) {
        if (facts.AllLiveAfter) {
            return StatusFlags.All;
        }
        return liveAtNext;
    }

    private static StatusFlags NodeLiveBefore(InstructionFlagFacts facts, StatusFlags liveAfter) {
        if (facts.AllLiveBefore) {
            return StatusFlags.All;
        }
        return facts.Effect.Reads | (liveAfter & ~facts.Effect.MustWrite);
    }
}
