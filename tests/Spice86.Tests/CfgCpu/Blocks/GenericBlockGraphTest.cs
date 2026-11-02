namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;

using Xunit;

/// <summary>
/// The block graph, dominator tree, natural loop forest and dataflow solver run on any block type.
/// </summary>
public sealed class GenericBlockGraphTest {
    private sealed record TestBlock(int Number);

    private static IComparer<TestBlock> Order { get; } = Comparer<TestBlock>.Create(
        (first, second) => first.Number.CompareTo(second.Number));

    private static TestBlock Block(int number) => new(number);

    private static BlockGraph<TestBlock> GraphOf(IReadOnlyList<TestBlock> blocks, IReadOnlyList<TestBlock> entries,
        IReadOnlyDictionary<TestBlock, IReadOnlyList<TestBlock>> successors) =>
        new(blocks, entries, successors, new Dictionary<TestBlock, TestBlock?>(),
            Order, block => $"block {block.Number}");

    private static BlockGraph<TestBlock> DiamondGraph() {
        TestBlock b0 = Block(0);
        TestBlock b1 = Block(1);
        TestBlock b2 = Block(2);
        TestBlock b3 = Block(3);
        return GraphOf(
            [b0, b1, b2, b3],
            [b0],
            new Dictionary<TestBlock, IReadOnlyList<TestBlock>> {
                [b0] = [b1, b2],
                [b1] = [b3],
                [b2] = [b3],
                [b3] = []
            });
    }

    [Fact]
    public void DominatorTree_OnTestBlocks_FindsImmediateDominators() {
        // Arrange
        BlockGraph<TestBlock> graph = DiamondGraph();
        TestBlock entry = graph.PrimaryEntryBlock;
        TestBlock join = graph.Blocks[3];

        // Act
        DominatorTree<TestBlock> dominatorTree = DominatorTreeBuilder.BuildFromEntries(graph);

        // Assert
        dominatorTree.GetImmediateDominator(join).Should().BeSameAs(entry);
        foreach (TestBlock block in graph.Blocks) {
            dominatorTree.Dominates(entry, block).Should().BeTrue();
        }
    }

    [Fact]
    public void NaturalLoopForest_OnTestBlocks_FindsLoop() {
        // Arrange
        TestBlock b0 = Block(0);
        TestBlock b1 = Block(1);
        TestBlock b2 = Block(2);
        TestBlock b3 = Block(3);
        BlockGraph<TestBlock> graph = GraphOf(
            [b0, b1, b2, b3],
            [b0],
            new Dictionary<TestBlock, IReadOnlyList<TestBlock>> {
                [b0] = [b1],
                [b1] = [b2],
                [b2] = [b1, b3],
                [b3] = []
            });
        DepthFirstOrdering<TestBlock> traversal = new(graph.EntryBlocks, graph.GetSuccessors);

        // Act
        NaturalLoopForest<TestBlock> forest = NaturalLoopForest<TestBlock>.Build(graph, traversal.RetreatingEdges);

        // Assert
        forest.Loops.Should().HaveCount(1);
        NaturalLoop<TestBlock> loop = forest.Loops[0];
        loop.Header.Should().Be(b1);
        loop.Blocks.Should().BeEquivalentTo([b1, b2]);
        loop.Latches.Should().Equal([b2]);
    }

    [Fact]
    public void ForwardDataflowSolver_OnTestBlocks_JoinsDiamondArms() {
        // Arrange
        BlockGraph<TestBlock> graph = DiamondGraph();
        TestBlock join = graph.Blocks[3];
        DepthFirstOrdering<TestBlock> traversal = new(graph.EntryBlocks, graph.GetSuccessors);
        Dictionary<TestBlock, int> written = new() { [graph.Blocks[1]] = 1, [graph.Blocks[2]] = 2 };

        // Act
        ForwardDataflowResult<TestBlock, HashSet<int>> result = ForwardDataflowSolver.Solve(
            graph, traversal,
            (block, state) => {
                HashSet<int> next = new(state);
                if (written.TryGetValue(block, out int value)) {
                    next.Add(value);
                }
                return next;
            },
            (first, second) => new HashSet<int>(first.Union(second)),
            new HashSet<int>(), new HashSet<int>());

        // Assert
        result.In[join].Should().BeEquivalentTo([1, 2]);
    }
}
