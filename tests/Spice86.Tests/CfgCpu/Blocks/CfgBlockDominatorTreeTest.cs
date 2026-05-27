namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.ControlFlowGraph.Analysis;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Tests reusable CFG block dominator analysis.
/// </summary>
public sealed class CfgBlockDominatorTreeTest : IDisposable {
    private const ushort Seg = 0x2000;

    private readonly LinkerHarness _harness = new();
    private readonly CfgBlockDominatorTreeBuilder _builder = new();

    public void Dispose() {
        _harness.Dispose();
    }

    [Fact]
    public void Build_WithMultipleEntries_DoesNotAssignSharedJoinToOneEntry() {
        // Arrange
        CfgInstruction firstEntry = CreateInstruction(0x0000);
        CfgInstruction firstOwned = CreateInstruction(0x0010);
        CfgInstruction secondEntry = CreateInstruction(0x0020);
        CfgInstruction secondOwned = CreateInstruction(0x0030);
        CfgInstruction sharedJoin = CreateInstruction(0x0040);
        _harness.Linker.Link(InstructionSuccessorType.Normal, firstEntry, firstOwned);
        _harness.Linker.Link(InstructionSuccessorType.Normal, firstOwned, sharedJoin);
        _harness.Linker.Link(InstructionSuccessorType.Normal, secondEntry, secondOwned);
        _harness.Linker.Link(InstructionSuccessorType.Normal, secondOwned, sharedJoin);
        CfgBlock firstEntryBlock = CfgTestHelpers.GetContainingBlock(firstEntry);
        CfgBlock firstOwnedBlock = CfgTestHelpers.GetContainingBlock(firstOwned);
        CfgBlock secondEntryBlock = CfgTestHelpers.GetContainingBlock(secondEntry);
        CfgBlock secondOwnedBlock = CfgTestHelpers.GetContainingBlock(secondOwned);
        CfgBlock sharedJoinBlock = CfgTestHelpers.GetContainingBlock(sharedJoin);
        List<CfgBlock> blocks = [firstEntryBlock, firstOwnedBlock, secondEntryBlock, secondOwnedBlock, sharedJoinBlock];
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock = new() {
            [firstEntryBlock] = [firstOwnedBlock],
            [firstOwnedBlock] = [sharedJoinBlock],
            [secondEntryBlock] = [secondOwnedBlock],
            [secondOwnedBlock] = [sharedJoinBlock]
        };

        // Act
        CfgBlockDominatorTree dominatorTree = _builder.BuildFromEntries(blocks, [firstEntryBlock, secondEntryBlock], successorsByBlock);

        // Assert
        dominatorTree.Dominates(firstEntryBlock, firstOwnedBlock).Should().BeTrue();
        dominatorTree.Dominates(secondEntryBlock, secondOwnedBlock).Should().BeTrue();
        dominatorTree.Dominates(firstEntryBlock, sharedJoinBlock).Should().BeFalse();
        dominatorTree.Dominates(secondEntryBlock, sharedJoinBlock).Should().BeFalse();
        dominatorTree.GetImmediateDominator(firstOwnedBlock).Should().BeSameAs(firstEntryBlock);
        dominatorTree.GetImmediateDominator(secondOwnedBlock).Should().BeSameAs(secondEntryBlock);
        dominatorTree.GetImmediateDominator(sharedJoinBlock).Should().BeNull();
    }

    [Fact]
    public void Dominates_WithEquivalentBlockId_ReturnsTrueForSelfDominance() {
        // Arrange
        CfgBlock entryBlock = new(100, CreateInstruction(0x0050));
        CfgBlock equivalentBlock = new(entryBlock.Id, CreateInstruction(0x0060));
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock = new();
        CfgBlockDominatorTree dominatorTree = _builder.Build([entryBlock], entryBlock, successorsByBlock);

        // Act
        bool dominates = dominatorTree.Dominates(equivalentBlock, equivalentBlock);

        // Assert
        dominates.Should().BeTrue();
    }

    [Fact]
    public void Build_WithLinearChain_AssignsTransitiveDominance() {
        // Arrange: A -> B -> C -> D
        CfgInstruction instrA = CreateInstruction(0x0000);
        CfgInstruction instrB = CreateInstruction(0x0010);
        CfgInstruction instrC = CreateInstruction(0x0020);
        CfgInstruction instrD = CreateInstruction(0x0030);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrA, instrB);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrB, instrC);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrC, instrD);
        CfgBlock blockA = CfgTestHelpers.GetContainingBlock(instrA);
        CfgBlock blockB = CfgTestHelpers.GetContainingBlock(instrB);
        CfgBlock blockC = CfgTestHelpers.GetContainingBlock(instrC);
        CfgBlock blockD = CfgTestHelpers.GetContainingBlock(instrD);
        List<CfgBlock> blocks = [blockA, blockB, blockC, blockD];
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock = new() {
            [blockA] = [blockB],
            [blockB] = [blockC],
            [blockC] = [blockD]
        };

        // Act
        CfgBlockDominatorTree dominatorTree = _builder.Build(blocks, blockA, successorsByBlock);

        // Assert — transitive dominance: A dominates everything
        dominatorTree.Dominates(blockA, blockB).Should().BeTrue();
        dominatorTree.Dominates(blockA, blockC).Should().BeTrue();
        dominatorTree.Dominates(blockA, blockD).Should().BeTrue();
        dominatorTree.Dominates(blockB, blockC).Should().BeTrue();
        dominatorTree.Dominates(blockB, blockD).Should().BeTrue();
        dominatorTree.Dominates(blockC, blockD).Should().BeTrue();
        // Reverse must not hold
        dominatorTree.Dominates(blockD, blockA).Should().BeFalse();
        dominatorTree.Dominates(blockC, blockB).Should().BeFalse();
        // Immediate dominators
        dominatorTree.GetImmediateDominator(blockB).Should().BeSameAs(blockA);
        dominatorTree.GetImmediateDominator(blockC).Should().BeSameAs(blockB);
        dominatorTree.GetImmediateDominator(blockD).Should().BeSameAs(blockC);
    }

    [Fact]
    public void Build_WithLoop_HeaderDominatesBody() {
        // Arrange: entry -> header -> body -> header (back-edge)
        CfgInstruction instrEntry = CreateInstruction(0x0000);
        CfgInstruction instrHeader = CreateInstruction(0x0010);
        CfgInstruction instrBody = CreateInstruction(0x0020);
        CfgInstruction instrExit = CreateInstruction(0x0030);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrEntry, instrHeader);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrHeader, instrBody);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrBody, instrHeader);
        _harness.Linker.Link(InstructionSuccessorType.Normal, instrHeader, instrExit);
        CfgBlock entryBlock = CfgTestHelpers.GetContainingBlock(instrEntry);
        CfgBlock headerBlock = CfgTestHelpers.GetContainingBlock(instrHeader);
        CfgBlock bodyBlock = CfgTestHelpers.GetContainingBlock(instrBody);
        CfgBlock exitBlock = CfgTestHelpers.GetContainingBlock(instrExit);
        List<CfgBlock> blocks = [entryBlock, headerBlock, bodyBlock, exitBlock];
        Dictionary<CfgBlock, IReadOnlyList<CfgBlock>> successorsByBlock = new() {
            [entryBlock] = [headerBlock],
            [headerBlock] = [bodyBlock, exitBlock],
            [bodyBlock] = [headerBlock]
        };

        // Act
        CfgBlockDominatorTree dominatorTree = _builder.Build(blocks, entryBlock, successorsByBlock);

        // Assert — header dominates body, body does not dominate header
        dominatorTree.Dominates(headerBlock, bodyBlock).Should().BeTrue();
        dominatorTree.Dominates(bodyBlock, headerBlock).Should().BeFalse();
        dominatorTree.Dominates(entryBlock, headerBlock).Should().BeTrue();
        dominatorTree.Dominates(entryBlock, exitBlock).Should().BeTrue();
        dominatorTree.GetImmediateDominator(bodyBlock).Should().BeSameAs(headerBlock);
        dominatorTree.GetImmediateDominator(headerBlock).Should().BeSameAs(entryBlock);
    }

    private CfgInstruction CreateInstruction(ushort offset) =>
        _harness.CreateInstruction(new SegmentedAddress(Seg, offset), 0xEB, 1, InstructionKind.Jump);
}