namespace Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The natural loops of one <see cref="PartitionBlockGraph"/> and how they nest. A back edge is an edge whose target
/// dominates its source (with every entry block as a root); the loop of a header is the header plus every block that
/// reaches one of its back-edge sources without passing through the header, and back edges that share a header form
/// one loop. Blocks that no entry reaches belong to no loop.
/// </summary>
internal sealed class NaturalLoopForest {
    private readonly Dictionary<CfgBlock, NaturalLoop> _loopByHeader;
    private readonly Dictionary<NaturalLoop, NaturalLoop?> _parentByLoop;
    private readonly Dictionary<NaturalLoop, IReadOnlyList<NaturalLoop>> _childrenByLoop;
    private readonly Dictionary<CfgBlock, NaturalLoop> _innermostByBlock;

    private NaturalLoopForest(IReadOnlyList<NaturalLoop> loops, IReadOnlyList<GraphEdge<CfgBlock>> irreducibleEdges,
        PartitionBlockGraph graph, CfgBlockDominatorTree dominators) {
        Loops = loops;
        IrreducibleEdges = irreducibleEdges;
        Graph = graph;
        Dominators = dominators;
        _loopByHeader = loops.ToDictionary(loop => loop.Header);
        Dictionary<CfgBlock, List<NaturalLoop>> loopsByBlock = [];
        foreach (NaturalLoop loop in loops) {
            foreach (CfgBlock block in loop.Blocks) {
                if (!loopsByBlock.TryGetValue(block, out List<NaturalLoop>? containing)) {
                    containing = [];
                    loopsByBlock.Add(block, containing);
                }
                containing.Add(loop);
            }
        }
        _parentByLoop = loops.ToDictionary(loop => loop, loop => FindParent(loop, loopsByBlock[loop.Header]));
        Dictionary<NaturalLoop, List<NaturalLoop>> childrenByLoop = loops.ToDictionary(loop => loop, _ => new List<NaturalLoop>());
        List<NaturalLoop> topLevelLoops = [];
        foreach (NaturalLoop loop in loops) {
            (_parentByLoop[loop] is NaturalLoop parent ? childrenByLoop[parent] : topLevelLoops).Add(loop);
        }
        _childrenByLoop = childrenByLoop.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<NaturalLoop>)pair.Value);
        TopLevelLoops = topLevelLoops;
        _innermostByBlock = loopsByBlock.ToDictionary(pair => pair.Key,
            pair => pair.Value.Aggregate((innermost, loop) => loop.Blocks.Count < innermost.Blocks.Count ? loop : innermost));
    }

    /// <summary>Every loop, in the <see cref="PartitionBlockGraph.Blocks"/> order of its header.</summary>
    public IReadOnlyList<NaturalLoop> Loops { get; }

    /// <summary>The loops that no other loop contains, in the order of <see cref="Loops"/>.</summary>
    public IReadOnlyList<NaturalLoop> TopLevelLoops { get; }

    /// <summary>
    /// The retreating edges whose source an entry reaches and whose target does not dominate the source: the
    /// cycles that are not natural loops. In the order of the retreating edges given to <see cref="Build"/>.
    /// </summary>
    public IReadOnlyList<GraphEdge<CfgBlock>> IrreducibleEdges { get; }

    /// <summary>The block graph the loops were found in.</summary>
    public PartitionBlockGraph Graph { get; }

    /// <summary>The dominator tree of <see cref="Graph"/>, every entry block a root.</summary>
    public CfgBlockDominatorTree Dominators { get; }

    /// <summary>The loop whose header is <paramref name="block"/>, or <c>null</c>.</summary>
    public NaturalLoop? LoopWithHeader(CfgBlock block) => _loopByHeader.GetValueOrDefault(block);

    /// <summary>The smallest loop that contains <paramref name="loop"/>, or <c>null</c> for a top-level loop.</summary>
    /// <exception cref="KeyNotFoundException">The loop is not part of this forest.</exception>
    public NaturalLoop? ParentOf(NaturalLoop loop) => _parentByLoop[loop];

    /// <summary>The loops whose parent is <paramref name="loop"/>, in the order of <see cref="Loops"/>.</summary>
    /// <exception cref="KeyNotFoundException">The loop is not part of this forest.</exception>
    public IReadOnlyList<NaturalLoop> ChildrenOf(NaturalLoop loop) => _childrenByLoop[loop];

    /// <summary>The smallest loop that contains <paramref name="block"/>, or <c>null</c> when no loop does.</summary>
    public NaturalLoop? InnermostLoopOf(CfgBlock block) => _innermostByBlock.GetValueOrDefault(block);

    /// <summary>The natural loops of <paramref name="graph"/>.</summary>
    /// <param name="graph">The block graph; every entry block is a dominator-tree root.</param>
    /// <param name="retreatingEdges">The retreating edges of a depth-first traversal of <paramref name="graph"/>.</param>
    /// <exception cref="InvalidOperationException">Two loops overlap without one containing the other.</exception>
    public static NaturalLoopForest Build(PartitionBlockGraph graph, IReadOnlyList<GraphEdge<CfgBlock>> retreatingEdges) {
        CfgBlockDominatorTree dominators = new CfgBlockDominatorTreeBuilder()
            .BuildFromEntries(graph.Blocks, graph.EntryBlocks, graph.SuccessorsByBlock);
        HashSet<CfgBlock> reachable = dominators.Blocks.ToHashSet();
        Dictionary<CfgBlock, int> indexByBlock = graph.Blocks
            .Select((block, index) => (block, index))
            .ToDictionary(pair => pair.block, pair => pair.index);
        List<NaturalLoop> loops = [];
        foreach (CfgBlock header in graph.Blocks) {
            List<CfgBlock> latches = graph.GetPredecessors(header)
                .Where(source => reachable.Contains(source) && dominators.Dominates(header, source))
                .ToList();
            if (latches.Count > 0) {
                loops.Add(BuildLoop(graph, header, latches, reachable, indexByBlock));
            }
        }
        List<GraphEdge<CfgBlock>> irreducibleEdges = retreatingEdges
            .Where(edge => reachable.Contains(edge.Source) && !dominators.Dominates(edge.Target, edge.Source))
            .ToList();
        return new NaturalLoopForest(loops, irreducibleEdges, graph, dominators);
    }

    private static NaturalLoop BuildLoop(PartitionBlockGraph graph, CfgBlock header, IReadOnlyList<CfgBlock> latches,
        IReadOnlySet<CfgBlock> reachable, IReadOnlyDictionary<CfgBlock, int> indexByBlock) {
        HashSet<CfgBlock> body = [header];
        Stack<CfgBlock> pending = new(latches);
        while (pending.Count > 0) {
            CfgBlock block = pending.Pop();
            if (!body.Add(block)) {
                continue;
            }
            foreach (CfgBlock predecessor in graph.GetPredecessors(block).Where(reachable.Contains)) {
                pending.Push(predecessor);
            }
        }
        List<CfgBlock> blocks = body.OrderBy(block => indexByBlock[block]).ToList();
        return new NaturalLoop(header, blocks, latches);
    }

    private static NaturalLoop? FindParent(NaturalLoop loop, IReadOnlyList<NaturalLoop> loops) {
        NaturalLoop? parent = null;
        foreach (NaturalLoop candidate in loops) {
            if (candidate == loop || !candidate.Contains(loop.Header)) {
                continue;
            }
            if (!loop.Blocks.All(candidate.Contains)) {
                throw new InvalidOperationException(
                    $"Natural loops with headers {loop.Header.Entry.Address} and {candidate.Header.Entry.Address} overlap without nesting.");
            }
            if (parent is null || candidate.Blocks.Count < parent.Blocks.Count) {
                parent = candidate;
            }
        }
        return parent;
    }
}