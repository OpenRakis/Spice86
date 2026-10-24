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

    private NaturalLoopForest(IReadOnlyList<NaturalLoop> loops, IReadOnlyList<GraphEdge<CfgBlock>> irreducibleEdges,
        PartitionBlockGraph graph, CfgBlockDominatorTree dominators) {
        Loops = loops;
        IrreducibleEdges = irreducibleEdges;
        Graph = graph;
        Dominators = dominators;
        _loopByHeader = loops.ToDictionary(loop => loop.Header);
        _parentByLoop = loops.ToDictionary(loop => loop, loop => FindParent(loop, loops));
        _childrenByLoop = loops.ToDictionary(loop => loop,
            loop => (IReadOnlyList<NaturalLoop>)loops.Where(candidate => _parentByLoop[candidate] == loop).ToList());
        TopLevelLoops = loops.Where(loop => _parentByLoop[loop] is null).ToList();
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
    public NaturalLoop? InnermostLoopOf(CfgBlock block) {
        NaturalLoop? innermost = null;
        foreach (NaturalLoop loop in Loops) {
            if (!loop.Contains(block)) {
                continue;
            }
            if (innermost is null || loop.Blocks.Count < innermost.Blocks.Count) {
                innermost = loop;
            }
        }
        return innermost;
    }

    /// <summary>The natural loops of <paramref name="graph"/>.</summary>
    /// <param name="graph">The block graph; every entry block is a dominator-tree root.</param>
    /// <param name="retreatingEdges">The retreating edges of a depth-first traversal of <paramref name="graph"/>.</param>
    /// <exception cref="InvalidOperationException">Two loops overlap without one containing the other.</exception>
    public static NaturalLoopForest Build(PartitionBlockGraph graph, IReadOnlyList<GraphEdge<CfgBlock>> retreatingEdges) {
        CfgBlockDominatorTree dominators = new CfgBlockDominatorTreeBuilder()
            .BuildFromEntries(graph.Blocks, graph.EntryBlocks, graph.SuccessorsByBlock);
        HashSet<CfgBlock> reachable = dominators.Blocks.ToHashSet();
        List<NaturalLoop> loops = [];
        foreach (CfgBlock header in graph.Blocks) {
            List<CfgBlock> latches = graph.GetPredecessors(header)
                .Where(source => reachable.Contains(source) && dominators.Dominates(header, source))
                .ToList();
            if (latches.Count > 0) {
                loops.Add(BuildLoop(graph, header, latches, reachable));
            }
        }
        List<GraphEdge<CfgBlock>> irreducibleEdges = retreatingEdges
            .Where(edge => reachable.Contains(edge.Source) && !dominators.Dominates(edge.Target, edge.Source))
            .ToList();
        return new NaturalLoopForest(loops, irreducibleEdges, graph, dominators);
    }

    private static NaturalLoop BuildLoop(PartitionBlockGraph graph, CfgBlock header, IReadOnlyList<CfgBlock> latches,
        IReadOnlySet<CfgBlock> reachable) {
        HashSet<CfgBlock> body = [header];
        Stack<CfgBlock> pending = new();
        foreach (CfgBlock latch in latches) {
            if (body.Add(latch)) {
                pending.Push(latch);
            }
        }
        while (pending.Count > 0) {
            CfgBlock block = pending.Pop();
            foreach (CfgBlock predecessor in graph.GetPredecessors(block)) {
                if (reachable.Contains(predecessor) && body.Add(predecessor)) {
                    pending.Push(predecessor);
                }
            }
        }
        List<CfgBlock> blocks = graph.Blocks.Where(body.Contains).ToList();
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