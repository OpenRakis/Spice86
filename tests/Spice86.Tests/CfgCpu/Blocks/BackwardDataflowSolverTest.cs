namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public sealed class BackwardDataflowSolverTest {
    [Fact]
    public void ValuesFlowBackwardThroughAStraightLine() {
        // Arrange
        CfgBlock first = Block(1, 0x00);
        CfgBlock second = Block(2, 0x10);
        CfgBlock third = Block(3, 0x20);
        PartitionBlockGraph graph = Graph([first, second, third], new() { [first] = [second], [second] = [third] });
        Dictionary<CfgBlock, int> generated = new() { [second] = 2, [third] = 1 };
        Dictionary<CfgBlock, int> killed = new() { [second] = 1 };

        // Act
        BackwardDataflowResult<int> result = Solve(graph, generated, killed);

        // Assert
        result.LiveIn[third].Should().Be(1);
        result.LiveOut[third].Should().Be(0);
        result.LiveOut[second].Should().Be(1);
        result.LiveIn[second].Should().Be(2);
        result.LiveOut[first].Should().Be(2);
        result.LiveIn[first].Should().Be(2);
    }

    [Fact]
    public void LiveOutIsTheJoinOfTheSuccessors() {
        // Arrange
        CfgBlock top = Block(1, 0x00);
        CfgBlock left = Block(2, 0x10);
        CfgBlock right = Block(3, 0x20);
        CfgBlock bottom = Block(4, 0x30);
        PartitionBlockGraph graph = Graph([top, left, right, bottom],
            new() { [top] = [left, right], [left] = [bottom], [right] = [bottom] });
        Dictionary<CfgBlock, int> generated = new() { [left] = 1, [right] = 2 };

        // Act
        BackwardDataflowResult<int> result = Solve(graph, generated, new Dictionary<CfgBlock, int>());

        // Assert
        result.LiveOut[top].Should().Be(3);
        result.LiveIn[bottom].Should().Be(0);
    }

    [Fact]
    public void LoopReachesAFixedPoint() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock head = Block(2, 0x10);
        CfgBlock latch = Block(3, 0x20);
        CfgBlock exit = Block(4, 0x30);
        PartitionBlockGraph graph = Graph([entry, head, latch, exit],
            new() { [entry] = [head], [head] = [latch], [latch] = [head, exit] });
        Dictionary<CfgBlock, int> generated = new() { [head] = 2, [exit] = 1 };
        Dictionary<CfgBlock, int> killed = new() { [latch] = 2 };

        // Act
        BackwardDataflowResult<int> result = Solve(graph, generated, killed);

        // Assert
        result.LiveIn[head].Should().Be(3);
        result.LiveIn[latch].Should().Be(1);
        result.LiveOut[latch].Should().Be(3);
        result.LiveOut[head].Should().Be(1);
        result.LiveIn[entry].Should().Be(3);
    }

    [Fact]
    public void BlockTheTraversalDidNotVisitIsSolvedToo() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock unreached = Block(2, 0x10);
        PartitionBlockGraph graph = Graph([entry, unreached], new() { [unreached] = [entry] });
        Dictionary<CfgBlock, int> generated = new() { [entry] = 4 };

        // Act
        BackwardDataflowResult<int> result = Solve(graph, generated, new Dictionary<CfgBlock, int>());

        // Assert
        result.LiveOut[unreached].Should().Be(4);
        result.LiveIn[unreached].Should().Be(4);
    }

    private static BackwardDataflowResult<int> Solve(PartitionBlockGraph graph, Dictionary<CfgBlock, int> generated, Dictionary<CfgBlock, int> killed) {
        DepthFirstOrdering<CfgBlock> traversal = new([graph.PrimaryEntryBlock], graph.GetSuccessors);
        return BackwardDataflowSolver.Solve(graph, traversal,
            (block, liveOut) => generated.GetValueOrDefault(block) | (liveOut & ~killed.GetValueOrDefault(block)),
            (first, second) => first | second, 0);
    }

    private static CfgBlock Block(int id, ushort offset) =>
        new(id, CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset)));

    private static PartitionBlockGraph Graph(IReadOnlyList<CfgBlock> blocks, Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors) =>
        new(blocks, [blocks[0]], successors, new Dictionary<CfgBlock, CfgBlock?>());
}
