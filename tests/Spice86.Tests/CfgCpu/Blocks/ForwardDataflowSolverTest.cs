namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public sealed class ForwardDataflowSolverTest {
    [Fact]
    public void ValuesFlowForwardThroughAStraightLine() {
        // Arrange
        CfgBlock first = Block(1, 0x00);
        CfgBlock second = Block(2, 0x10);
        CfgBlock third = Block(3, 0x20);
        BlockGraph<CfgBlock> graph = Graph([first, second, third],
            [first], new() { [first] = [second], [second] = [third] });
        Dictionary<CfgBlock, int> added = new() { [first] = 10, [second] = 100, [third] = 1000 };

        // Act
        ForwardDataflowResult<CfgBlock, int> result = Solve(graph, added, 1);

        // Assert
        result.In[first].Should().Be(1);
        result.Out[first].Should().Be(11);
        result.In[second].Should().Be(11);
        result.Out[second].Should().Be(111);
        result.In[third].Should().Be(111);
        result.Out[third].Should().Be(1111);
    }

    [Fact]
    public void InIsTheJoinOfThePredecessors() {
        // Arrange
        CfgBlock top = Block(1, 0x00);
        CfgBlock left = Block(2, 0x10);
        CfgBlock right = Block(3, 0x20);
        CfgBlock bottom = Block(4, 0x30);
        BlockGraph<CfgBlock> graph = Graph([top, left, right, bottom],
            [top], new() { [top] = [left, right], [left] = [bottom], [right] = [bottom] });
        Dictionary<CfgBlock, int> added = new() { [left] = 1, [right] = 2 };

        // Act
        ForwardDataflowResult<CfgBlock, int> result = SolveWithOr(graph, added, 0);

        // Assert
        result.In[bottom].Should().Be(3);
        result.In[top].Should().Be(0);
    }

    [Fact]
    public void LoopReachesAFixedPoint() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock head = Block(2, 0x10);
        CfgBlock latch = Block(3, 0x20);
        BlockGraph<CfgBlock> graph = Graph([entry, head, latch],
            [entry], new() { [entry] = [head], [head] = [latch], [latch] = [head] });
        Dictionary<CfgBlock, int> added = new() { [entry] = 1, [head] = 2, [latch] = 4 };

        // Act
        ForwardDataflowResult<CfgBlock, int> result = SolveWithOr(graph, added, 0);

        // Assert
        result.Out[entry].Should().Be(1);
        result.In[head].Should().Be(7);
        result.Out[head].Should().Be(7);
        result.In[latch].Should().Be(7);
        result.Out[latch].Should().Be(7);
    }

    [Fact]
    public void EveryEntryBlockReceivesTheEntryValue() {
        // Arrange
        CfgBlock first = Block(1, 0x00);
        CfgBlock second = Block(2, 0x10);
        BlockGraph<CfgBlock> graph = Graph([first, second],
            [first, second], new Dictionary<CfgBlock, IReadOnlyList<CfgBlock>>());
        Dictionary<CfgBlock, int> added = new() { [first] = 10 };

        // Act
        ForwardDataflowResult<CfgBlock, int> result = Solve(graph, added, 5);

        // Assert
        result.In[first].Should().Be(5);
        result.Out[first].Should().Be(15);
        result.In[second].Should().Be(5);
        result.Out[second].Should().Be(5);
    }

    [Fact]
    public void BlockTheTraversalDidNotVisitIsSolvedToo() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock middle = Block(2, 0x10);
        CfgBlock unreached = Block(3, 0x20);
        BlockGraph<CfgBlock> graph = Graph([entry, middle, unreached],
            [entry], new() { [middle] = [unreached] });
        Dictionary<CfgBlock, int> added = new() { [middle] = 2, [unreached] = 4 };

        // Act
        ForwardDataflowResult<CfgBlock, int> result = Solve(graph, added, 8);

        // Assert
        result.In[middle].Should().Be(0);
        result.Out[middle].Should().Be(2);
        result.In[unreached].Should().Be(2);
        result.Out[unreached].Should().Be(6);
    }

    private static ForwardDataflowResult<CfgBlock, int> Solve(BlockGraph<CfgBlock> graph, Dictionary<CfgBlock, int> added,
        int entryValue) {
        DepthFirstOrdering<CfgBlock> traversal = new(graph.EntryBlocks, graph.GetSuccessors);
        return ForwardDataflowSolver.Solve(graph, traversal,
            (block, inValue) => inValue + added.GetValueOrDefault(block),
            (first, second) => first + second, 0, entryValue);
    }

    private static ForwardDataflowResult<CfgBlock, int> SolveWithOr(BlockGraph<CfgBlock> graph,
        Dictionary<CfgBlock, int> added, int entryValue) {
        DepthFirstOrdering<CfgBlock> traversal = new(graph.EntryBlocks, graph.GetSuccessors);
        return ForwardDataflowSolver.Solve(graph, traversal,
            (block, inValue) => inValue | added.GetValueOrDefault(block),
            (first, second) => first | second, 0, entryValue);
    }

    private static CfgBlock Block(int id, ushort offset) =>
        new(id, CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset)));

    private static BlockGraph<CfgBlock> Graph(IReadOnlyList<CfgBlock> blocks, IReadOnlyList<CfgBlock> entryBlocks,
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors) =>
        new(blocks, entryBlocks, successors, new Dictionary<CfgBlock, CfgBlock?>(), CfgBlockOrder.Instance, block => block.Entry.Address.ToString());
}
