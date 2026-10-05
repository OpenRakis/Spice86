namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;
using Xunit;

public sealed class PartitionBlockGraphTest {
    private static CfgInstruction Jcc(ushort offset) {
        return CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset), 0x74, 2, InstructionKind.Jump);
    }

    private static CfgInstruction Jmp(ushort offset) {
        return CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset), 0xEB, 2, InstructionKind.Jump);
    }

    private static CfgInstruction Nop(ushort offset) {
        return CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset), 0x90, 1, InstructionKind.None);
    }

    private static CfgBlock CreateBlock(int id, ICfgNode entry) {
        CfgBlock block = new CfgBlock(id, entry);
        entry.ContainingBlock = block;
        return block;
    }

    private static CfgCodePartition CreatePartition(IReadOnlyList<CfgBlock> blocks, IReadOnlyList<ICfgNode> entryNodes) {
        List<CfgCodePartitionEntry> entries = new List<CfgCodePartitionEntry>();
        foreach (ICfgNode node in entryNodes) {
            entries.Add(new CfgCodePartitionEntry { Node = node, Kind = CfgCodePartitionEntryKind.FunctionEntry });
        }
        return new CfgCodePartition {
            Id = 1,
            Kind = CfgCodePartitionKind.Observed,
            Name = "partition_1",
            Blocks = blocks,
            Entries = entries
        };
    }

    [Fact]
    public void FromPartition_ConditionalJump_FallthroughIsNotTakenBlock() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Jcc(0x0000));
        CfgBlock block101 = CreateBlock(101, Nop(0x0002));
        CfgBlock block102 = CreateBlock(102, Nop(0x0010));
        block100.Terminator.Successors.Add(block102.Entry);
        block100.Terminator.Successors.Add(block101.Entry);
        CfgCodePartition partition = CreatePartition(new List<CfgBlock> { block102, block100, block101 }, new List<ICfgNode> { block100.Entry });

        // Act
        PartitionBlockGraph graph = PartitionBlockGraph.FromPartition(partition, block100.Entry);

        // Assert
        graph.GetFallthroughSuccessor(block100).Should().BeSameAs(block101);
        graph.GetFallthroughSuccessor(block101).Should().BeNull();
        graph.GetFallthroughSuccessor(block102).Should().BeNull();
    }

    [Fact]
    public void FromPartition_OrdersBlocksSuccessorsAndPredecessorsByAddress() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Jcc(0x0000));
        CfgBlock block101 = CreateBlock(101, Nop(0x0002));
        CfgBlock block102 = CreateBlock(102, Nop(0x0010));
        block100.Terminator.Successors.Add(block102.Entry);
        block100.Terminator.Successors.Add(block101.Entry);
        CfgCodePartition partition = CreatePartition(new List<CfgBlock> { block102, block100, block101 }, new List<ICfgNode> { block100.Entry });

        // Act
        PartitionBlockGraph graph = PartitionBlockGraph.FromPartition(partition, block100.Entry);

        // Assert
        graph.Blocks.Should().Equal(block100, block101, block102);
        graph.GetSuccessors(block100).Should().Equal(block101, block102);
        graph.GetSuccessors(block101).Should().BeEmpty();
        graph.GetSuccessors(block102).Should().BeEmpty();
        graph.GetPredecessors(block102).Should().Equal(block100);
        graph.GetPredecessors(block100).Should().BeEmpty();
        graph.SuccessorsByBlock.Should().HaveCount(3);
        graph.PredecessorsByBlock.Should().HaveCount(3);
    }

    [Fact]
    public void FromPartition_SuccessorOutsidePartition_IsExcluded() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Jmp(0x0000));
        CfgBlock block101 = CreateBlock(101, Nop(0x0010));
        CfgBlock block102 = CreateBlock(102, Nop(0x0020));
        block100.Terminator.Successors.Add(block101.Entry);
        block100.Terminator.Successors.Add(block102.Entry);
        CfgCodePartition partition = CreatePartition(new List<CfgBlock> { block100, block101 }, new List<ICfgNode> { block100.Entry });

        // Act
        PartitionBlockGraph graph = PartitionBlockGraph.FromPartition(partition, block100.Entry);

        // Assert
        graph.GetSuccessors(block100).Should().Equal(block101);
        graph.GetFallthroughSuccessor(block100).Should().BeNull();
        graph.Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void FromPartition_SelectorTerminator_HasNoFallthrough() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Jmp(0x0000));
        SelectorNode selector = new SelectorNode(1000000, new SegmentedAddress(0x1000, 0x0002));
        CfgBlock block101 = CreateBlock(101, selector);
        CfgBlock block102 = CreateBlock(102, Nop(0x0002));
        block100.Terminator.Successors.Add(selector);
        selector.Successors.Add(block102.Entry);
        CfgCodePartition partition = CreatePartition(new List<CfgBlock> { block100, block101, block102 }, new List<ICfgNode> { block100.Entry });

        // Act
        PartitionBlockGraph graph = PartitionBlockGraph.FromPartition(partition, block100.Entry);

        // Assert
        graph.GetSuccessors(block101).Should().Equal(block102);
        graph.GetFallthroughSuccessor(block101).Should().BeNull();
        graph.GetFallthroughSuccessor(block100).Should().BeSameAs(block101);
    }

    [Fact]
    public void FromPartition_SuccessorInsideAnotherBlock_Throws() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Jmp(0x0000));
        CfgBlock block101 = CreateBlock(101, Nop(0x0010));
        ICfgNode secondInstr = Nop(0x0011);
        block101.Append(secondInstr);
        secondInstr.ContainingBlock = block101;
        block100.Terminator.Successors.Add(secondInstr);
        CfgCodePartition partition = CreatePartition(new List<CfgBlock> { block100, block101 }, new List<ICfgNode> { block100.Entry });

        // Act
        Action act = () => PartitionBlockGraph.FromPartition(partition, block100.Entry);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*1000:0011*1000:0000*1000:0010*");
    }

    [Fact]
    public void FromPartition_ExplicitPrimaryEntry_IsFirstEntryBlock() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Jmp(0x0000));
        CfgBlock block101 = CreateBlock(101, Jmp(0x0010));
        CfgBlock block102 = CreateBlock(102, Jmp(0x0020));
        CfgCodePartition partition = CreatePartition(
            new List<CfgBlock> { block100, block101, block102 },
            new List<ICfgNode> { block100.Entry, block101.Entry, block102.Entry });

        // Act
        PartitionBlockGraph graph = PartitionBlockGraph.FromPartition(partition, block102.Entry);

        // Assert
        graph.EntryBlocks.Should().Equal(block102, block100, block101);
        graph.PrimaryEntryBlock.Should().BeSameAs(block102);
    }

    [Fact]
    public void FromPartition_PrimaryEntryOutsidePartition_Throws() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Jmp(0x0000));
        ICfgNode externalNode = Nop(0x0040);
        CfgCodePartition partition = CreatePartition(new List<CfgBlock> { block100 }, new List<ICfgNode> { block100.Entry });

        // Act
        Action act = () => PartitionBlockGraph.FromPartition(partition, externalNode);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*1000:0040*");
    }

    [Fact]
    public void FromPartition_SelfLoop_IsOwnSuccessorAndPredecessor() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Jmp(0x0000));
        block100.Terminator.Successors.Add(block100.Entry);
        CfgCodePartition partition = CreatePartition(new List<CfgBlock> { block100 }, new List<ICfgNode> { block100.Entry });

        // Act
        PartitionBlockGraph graph = PartitionBlockGraph.FromPartition(partition, block100.Entry);

        // Assert
        graph.GetSuccessors(block100).Should().Equal(block100);
        graph.GetPredecessors(block100).Should().Equal(block100);
        graph.GetFallthroughSuccessor(block100).Should().BeNull();
    }

    [Fact]
    public void Constructor_MissingSuccessorKey_MeansNoSuccessors() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Nop(0x0000));
        CfgBlock block101 = CreateBlock(101, Nop(0x0010));
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors = new Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> {
            [block100] = new List<CfgBlock> { block101 }
        };
        Dictionary<CfgBlock, CfgBlock?> fallthrough = new Dictionary<CfgBlock, CfgBlock?>();

        // Act
        PartitionBlockGraph graph = new PartitionBlockGraph(
            new List<CfgBlock> { block100, block101 },
            new List<CfgBlock> { block100 },
            successors,
            fallthrough);

        // Assert
        graph.GetSuccessors(block101).Should().BeEmpty();
        graph.GetPredecessors(block101).Should().Equal(block100);
        graph.GetFallthroughSuccessor(block100).Should().BeNull();
    }

    [Fact]
    public void Constructor_SuccessorNotInBlocks_Throws() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Nop(0x0000));
        CfgBlock block101 = CreateBlock(101, Nop(0x0010));
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors = new Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> {
            [block100] = new List<CfgBlock> { block101 }
        };
        Dictionary<CfgBlock, CfgBlock?> fallthrough = new Dictionary<CfgBlock, CfgBlock?>();

        // Act
        Action act = () => new PartitionBlockGraph(
            new List<CfgBlock> { block100 },
            new List<CfgBlock> { block100 },
            successors,
            fallthrough);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_FallthroughNotASuccessor_Throws() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Nop(0x0000));
        CfgBlock block101 = CreateBlock(101, Nop(0x0010));
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors = new Dictionary<CfgBlock, IReadOnlyList<CfgBlock>>();
        Dictionary<CfgBlock, CfgBlock?> fallthrough = new Dictionary<CfgBlock, CfgBlock?> {
            [block100] = block101,
            [block101] = null
        };

        // Act
        Action act = () => new PartitionBlockGraph(
            new List<CfgBlock> { block100, block101 },
            new List<CfgBlock> { block100 },
            successors,
            fallthrough);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NoEntryBlock_Throws() {
        // Arrange
        CfgBlock block100 = CreateBlock(100, Nop(0x0000));
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successors = new Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> {
            [block100] = new List<CfgBlock>()
        };
        Dictionary<CfgBlock, CfgBlock?> fallthrough = new Dictionary<CfgBlock, CfgBlock?> {
            [block100] = null
        };

        // Act
        Action act = () => new PartitionBlockGraph(
            new List<CfgBlock> { block100 },
            new List<CfgBlock>(),
            successors,
            fallthrough);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}