namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The natural loops of one <see cref="BlockGraph{TBlock}"/> and how they nest. A back edge is an edge whose target
/// dominates its source (with every entry block as a root); the loop of a header is the header plus every block that
/// reaches one of its back-edge sources without passing through the header, and back edges that share a header form
/// one loop. Blocks that no entry reaches belong to no loop.
/// </summary>
/// <typeparam name="TBlock">The block type; used for identity only.</typeparam>
internal sealed class NaturalLoopForest<TBlock> where TBlock : class {
    private readonly Dictionary<TBlock, NaturalLoop<TBlock>> _loopByHeader;
    private readonly Dictionary<NaturalLoop<TBlock>, NaturalLoop<TBlock>?> _parentByLoop;
    private readonly Dictionary<NaturalLoop<TBlock>, IReadOnlyList<NaturalLoop<TBlock>>> _childrenByLoop;
    private readonly Dictionary<TBlock, NaturalLoop<TBlock>> _innermostByBlock;

    private NaturalLoopForest(IReadOnlyList<NaturalLoop<TBlock>> loops, IReadOnlyList<GraphEdge<TBlock>> irreducibleEdges,
        BlockGraph<TBlock> graph, DominatorTree<TBlock> dominators) {
        Loops = loops;
        IrreducibleEdges = irreducibleEdges;
        Graph = graph;
        Dominators = dominators;
        _loopByHeader = loops.ToDictionary(loop => loop.Header);
        Dictionary<TBlock, List<NaturalLoop<TBlock>>> loopsByBlock = [];
        foreach (NaturalLoop<TBlock> loop in loops) {
            foreach (TBlock block in loop.Blocks) {
                if (!loopsByBlock.TryGetValue(block, out List<NaturalLoop<TBlock>>? containing)) {
                    containing = [];
                    loopsByBlock.Add(block, containing);
                }
                containing.Add(loop);
            }
        }
        _parentByLoop = loops.ToDictionary(loop => loop, loop => FindParent(loop, loopsByBlock[loop.Header], graph));
        Dictionary<NaturalLoop<TBlock>, List<NaturalLoop<TBlock>>> childrenByLoop = loops.ToDictionary(loop => loop, _ => new List<NaturalLoop<TBlock>>());
        List<NaturalLoop<TBlock>> topLevelLoops = [];
        foreach (NaturalLoop<TBlock> loop in loops) {
            (_parentByLoop[loop] is NaturalLoop<TBlock> parent ? childrenByLoop[parent] : topLevelLoops).Add(loop);
        }
        _childrenByLoop = childrenByLoop.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<NaturalLoop<TBlock>>)pair.Value);
        TopLevelLoops = topLevelLoops;
        _innermostByBlock = loopsByBlock.ToDictionary(pair => pair.Key,
            pair => pair.Value.Aggregate((innermost, loop) => loop.Blocks.Count < innermost.Blocks.Count ? loop : innermost));
    }

    /// <summary>Every loop, in the <see cref="BlockGraph{TBlock}.Blocks"/> order of its header.</summary>
    public IReadOnlyList<NaturalLoop<TBlock>> Loops { get; }

    /// <summary>The loops that no other loop contains, in the order of <see cref="Loops"/>.</summary>
    public IReadOnlyList<NaturalLoop<TBlock>> TopLevelLoops { get; }

    /// <summary>
    /// The retreating edges whose source an entry reaches and whose target does not dominate the source: the
    /// cycles that are not natural loops. In the order of the retreating edges given to <see cref="Build"/>.
    /// </summary>
    public IReadOnlyList<GraphEdge<TBlock>> IrreducibleEdges { get; }

    /// <summary>The block graph the loops were found in.</summary>
    public BlockGraph<TBlock> Graph { get; }

    /// <summary>The dominator tree of <see cref="Graph"/>, every entry block a root.</summary>
    public DominatorTree<TBlock> Dominators { get; }

    /// <summary>The loop whose header is <paramref name="block"/>, or <c>null</c>.</summary>
    public NaturalLoop<TBlock>? LoopWithHeader(TBlock block) => _loopByHeader.GetValueOrDefault(block);

    /// <summary>The smallest loop that contains <paramref name="loop"/>, or <c>null</c> for a top-level loop.</summary>
    /// <exception cref="KeyNotFoundException">The loop is not part of this forest.</exception>
    public NaturalLoop<TBlock>? ParentOf(NaturalLoop<TBlock> loop) => _parentByLoop[loop];

    /// <summary>The loops whose parent is <paramref name="loop"/>, in the order of <see cref="Loops"/>.</summary>
    /// <exception cref="KeyNotFoundException">The loop is not part of this forest.</exception>
    public IReadOnlyList<NaturalLoop<TBlock>> ChildrenOf(NaturalLoop<TBlock> loop) => _childrenByLoop[loop];

    /// <summary>The smallest loop that contains <paramref name="block"/>, or <c>null</c> when no loop does.</summary>
    public NaturalLoop<TBlock>? InnermostLoopOf(TBlock block) => _innermostByBlock.GetValueOrDefault(block);

    /// <summary>The natural loops of <paramref name="graph"/>.</summary>
    /// <param name="graph">The block graph; every entry block is a dominator-tree root.</param>
    /// <param name="retreatingEdges">The retreating edges of a depth-first traversal of <paramref name="graph"/>.</param>
    /// <exception cref="InvalidOperationException">Two loops overlap without one containing the other.</exception>
    public static NaturalLoopForest<TBlock> Build(BlockGraph<TBlock> graph, IReadOnlyList<GraphEdge<TBlock>> retreatingEdges) {
        DominatorTree<TBlock> dominators = DominatorTreeBuilder.BuildFromEntries(graph);
        return Build(graph, retreatingEdges, dominators);
    }

    /// <summary>The natural loops of <paramref name="graph"/>, reusing the dominator tree <paramref name="dominators"/> built over the same entries.</summary>
    /// <param name="graph">The block graph.</param>
    /// <param name="retreatingEdges">The retreating edges of a depth-first traversal of <paramref name="graph"/>.</param>
    /// <param name="dominators">The dominator tree of <paramref name="graph"/>, every entry block a root.</param>
    /// <exception cref="InvalidOperationException">Two loops overlap without one containing the other.</exception>
    public static NaturalLoopForest<TBlock> Build(BlockGraph<TBlock> graph, IReadOnlyList<GraphEdge<TBlock>> retreatingEdges,
        DominatorTree<TBlock> dominators) {
        HashSet<TBlock> reachable = dominators.Blocks.ToHashSet();
        Dictionary<TBlock, int> indexByBlock = graph.Blocks
            .Select((block, index) => (block, index))
            .ToDictionary(pair => pair.block, pair => pair.index);
        List<NaturalLoop<TBlock>> loops = [];
        foreach (TBlock header in graph.Blocks) {
            List<TBlock> latches = graph.GetPredecessors(header)
                .Where(source => reachable.Contains(source) && dominators.Dominates(header, source))
                .ToList();
            if (latches.Count > 0) {
                loops.Add(BuildLoop(graph, header, latches, reachable, indexByBlock));
            }
        }
        List<GraphEdge<TBlock>> irreducibleEdges = retreatingEdges
            .Where(edge => reachable.Contains(edge.Source) && !dominators.Dominates(edge.Target, edge.Source))
            .ToList();
        return new NaturalLoopForest<TBlock>(loops, irreducibleEdges, graph, dominators);
    }

    private static NaturalLoop<TBlock> BuildLoop(BlockGraph<TBlock> graph, TBlock header, IReadOnlyList<TBlock> latches,
        IReadOnlySet<TBlock> reachable, IReadOnlyDictionary<TBlock, int> indexByBlock) {
        HashSet<TBlock> body = [header];
        Stack<TBlock> pending = new(latches);
        while (pending.Count > 0) {
            TBlock block = pending.Pop();
            if (!body.Add(block)) {
                continue;
            }
            foreach (TBlock predecessor in graph.GetPredecessors(block).Where(reachable.Contains)) {
                pending.Push(predecessor);
            }
        }
        List<TBlock> blocks = body.OrderBy(block => indexByBlock[block]).ToList();
        return new NaturalLoop<TBlock>(header, blocks, latches);
    }

    private static NaturalLoop<TBlock>? FindParent(NaturalLoop<TBlock> loop, IReadOnlyList<NaturalLoop<TBlock>> loops,
        BlockGraph<TBlock> graph) {
        NaturalLoop<TBlock>? parent = null;
        foreach (NaturalLoop<TBlock> candidate in loops) {
            if (candidate == loop || !candidate.Contains(loop.Header)) {
                continue;
            }
            if (!loop.Blocks.All(candidate.Contains)) {
                throw new InvalidOperationException(
                    $"Natural loops with headers {graph.Describe(loop.Header)} and {graph.Describe(candidate.Header)} overlap without nesting.");
            }
            if (parent is null || candidate.Blocks.Count < parent.Blocks.Count) {
                parent = candidate;
            }
        }
        return parent;
    }
}
