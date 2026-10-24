namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public sealed class NaturalLoopForestTest {
    [Fact]
    public void StraightLineHasNoLoop() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock middle = Block(2, 0x10);
        CfgBlock exit = Block(3, 0x20);
        PartitionBlockGraph graph = Graph([entry, middle, exit], [entry], new() { [entry] = [middle], [middle] = [exit] });

        // Act
        NaturalLoopForest forest = Build(graph);

        // Assert
        forest.Loops.Should().BeEmpty();
        forest.IrreducibleEdges.Should().BeEmpty();
        forest.InnermostLoopOf(middle).Should().BeNull();
    }

    [Fact]
    public void SelfLoopIsALoopOfOneBlock() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock spin = Block(2, 0x10);
        CfgBlock exit = Block(3, 0x20);
        PartitionBlockGraph graph = Graph([entry, spin, exit], [entry], new() { [entry] = [spin], [spin] = [spin, exit] });

        // Act
        NaturalLoopForest forest = Build(graph);

        // Assert
        forest.Loops.Should().HaveCount(1);
        NaturalLoop loop = forest.Loops[0];
        loop.Header.Should().BeSameAs(spin);
        loop.Blocks.Should().Equal(spin);
        loop.Latches.Should().Equal(spin);
        forest.LoopWithHeader(spin).Should().BeSameAs(loop);
        forest.InnermostLoopOf(spin).Should().BeSameAs(loop);
        forest.InnermostLoopOf(exit).Should().BeNull();
        forest.IrreducibleEdges.Should().BeEmpty();
    }

    [Fact]
    public void LoopIsEveryBlockThatReachesTheLatchWithoutTheHeader() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock head = Block(2, 0x10);
        CfgBlock body = Block(3, 0x20);
        CfgBlock latch = Block(4, 0x30);
        CfgBlock exit = Block(5, 0x40);
        PartitionBlockGraph graph = Graph([entry, head, body, latch, exit], [entry],
            new() { [entry] = [head], [head] = [body, exit], [body] = [latch], [latch] = [head] });

        // Act
        NaturalLoopForest forest = Build(graph);

        // Assert
        forest.Loops.Should().HaveCount(1);
        NaturalLoop loop = forest.Loops[0];
        loop.Header.Should().BeSameAs(head);
        loop.Blocks.Should().Equal(head, body, latch);
        loop.Latches.Should().Equal(latch);
        loop.Contains(exit).Should().BeFalse();
        forest.LoopWithHeader(body).Should().BeNull();
    }

    [Fact]
    public void BackEdgesToOneHeaderFormOneLoop() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock head = Block(2, 0x10);
        CfgBlock left = Block(3, 0x20);
        CfgBlock right = Block(4, 0x30);
        CfgBlock exit = Block(5, 0x40);
        PartitionBlockGraph graph = Graph([entry, head, left, right, exit], [entry],
            new() { [entry] = [head], [head] = [left, right], [left] = [head], [right] = [head, exit] });

        // Act
        NaturalLoopForest forest = Build(graph);

        // Assert
        forest.Loops.Should().HaveCount(1);
        NaturalLoop loop = forest.Loops[0];
        loop.Blocks.Should().Equal(head, left, right);
        loop.Latches.Should().Equal(left, right);
    }

    [Fact]
    public void NestedLoopsKnowTheirParentAndChildren() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock outer = Block(2, 0x10);
        CfgBlock inner = Block(3, 0x20);
        CfgBlock latch = Block(4, 0x30);
        CfgBlock exit = Block(5, 0x40);
        PartitionBlockGraph graph = Graph([entry, outer, inner, latch, exit], [entry],
            new() { [entry] = [outer], [outer] = [inner], [inner] = [inner, latch], [latch] = [outer, exit] });

        // Act
        NaturalLoopForest forest = Build(graph);

        // Assert
        forest.Loops.Should().HaveCount(2);
        NaturalLoop outerLoop = forest.Loops[0];
        NaturalLoop innerLoop = forest.Loops[1];
        outerLoop.Header.Should().BeSameAs(outer);
        outerLoop.Blocks.Should().Equal(outer, inner, latch);
        innerLoop.Header.Should().BeSameAs(inner);
        innerLoop.Blocks.Should().Equal(inner);
        forest.ParentOf(innerLoop).Should().BeSameAs(outerLoop);
        forest.ParentOf(outerLoop).Should().BeNull();
        forest.ChildrenOf(outerLoop).Should().Equal(innerLoop);
        forest.ChildrenOf(innerLoop).Should().BeEmpty();
        forest.TopLevelLoops.Should().Equal(outerLoop);
        forest.InnermostLoopOf(inner).Should().BeSameAs(innerLoop);
        forest.InnermostLoopOf(latch).Should().BeSameAs(outerLoop);
    }

    [Fact]
    public void CycleEnteredAtTwoBlocksIsIrreducible() {
        // Arrange
        CfgBlock first = Block(1, 0x00);
        CfgBlock second = Block(2, 0x08);
        CfgBlock left = Block(3, 0x10);
        CfgBlock right = Block(4, 0x20);
        PartitionBlockGraph graph = Graph([first, second, left, right], [first, second],
            new() { [first] = [left], [second] = [right], [left] = [right], [right] = [left] });

        // Act
        NaturalLoopForest forest = Build(graph);

        // Assert
        forest.Loops.Should().BeEmpty();
        forest.IrreducibleEdges.Should().Equal(new GraphEdge<CfgBlock>(right, left));
    }

    [Fact]
    public void PartitionEntryInsideACyclePreventsTheLoop() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock head = Block(2, 0x10);
        CfgBlock middle = Block(3, 0x20);
        CfgBlock exit = Block(4, 0x30);
        PartitionBlockGraph graph = Graph([entry, head, middle, exit], [entry, middle],
            new() { [entry] = [head], [head] = [middle], [middle] = [head, exit] });

        // Act
        NaturalLoopForest forest = Build(graph);

        // Assert
        forest.Loops.Should().BeEmpty();
        forest.IrreducibleEdges.Should().Equal(new GraphEdge<CfgBlock>(middle, head));
    }

    [Fact]
    public void EntryBlockCanBeALoopHeader() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock head = Block(2, 0x10);
        CfgBlock body = Block(3, 0x20);
        CfgBlock exit = Block(4, 0x30);
        PartitionBlockGraph graph = Graph([entry, head, body, exit], [entry, head],
            new() { [entry] = [head], [head] = [body, exit], [body] = [head] });

        // Act
        NaturalLoopForest forest = Build(graph);

        // Assert
        forest.Loops.Should().HaveCount(1);
        forest.Loops[0].Header.Should().BeSameAs(head);
        forest.Loops[0].Blocks.Should().Equal(head, body);
        forest.IrreducibleEdges.Should().BeEmpty();
    }

    [Fact]
    public void BlocksNoEntryReachesAreInNoLoop() {
        // Arrange
        CfgBlock entry = Block(1, 0x00);
        CfgBlock head = Block(2, 0x10);
        CfgBlock body = Block(3, 0x20);
        CfgBlock exit = Block(4, 0x30);
        CfgBlock dead = Block(5, 0x40);
        CfgBlock deadSpin = Block(6, 0x50);
        PartitionBlockGraph graph = Graph([entry, head, body, exit, dead, deadSpin], [entry],
            new() {
                [entry] = [head], [head] = [body, exit], [body] = [head],
                [dead] = [body], [deadSpin] = [deadSpin]
            });

        // Act
        NaturalLoopForest forest = Build(graph);

        // Assert
        forest.Loops.Should().HaveCount(1);
        forest.Loops[0].Blocks.Should().Equal(head, body);
        forest.LoopWithHeader(deadSpin).Should().BeNull();
        forest.IrreducibleEdges.Should().BeEmpty();
    }

    private static NaturalLoopForest Build(PartitionBlockGraph graph) {
        DepthFirstOrdering<CfgBlock> traversal = new([.. graph.EntryBlocks, .. graph.Blocks], graph.GetSuccessors);
        return NaturalLoopForest.Build(graph, traversal.RetreatingEdges);
    }

    private static CfgBlock Block(int id, ushort offset) =>
        new(id, CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset)));

    private static PartitionBlockGraph Graph(IReadOnlyList<CfgBlock> blocks, IReadOnlyList<CfgBlock> entries,
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors) =>
        new(blocks, entries, successors, new Dictionary<CfgBlock, CfgBlock?>());
}