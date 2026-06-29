namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Tests for block integrity after speculative reachability sweep.
/// Validates fully-speculative blocks are removed wholesale, mixed blocks are truncated,
/// and observed blocks losing a successor edge keep their structure.
/// </summary>
public sealed class SpeculativeBlockIntegrityTest : SpeculativeTestBase {
    private readonly SpeculativeReachabilityPruner _pruner;

    public SpeculativeBlockIntegrityTest() {
        _pruner = new SpeculativeReachabilityPruner(NodeLinker, NodeIndex);
    }

    /// <summary>
    /// Fully-speculative block removed wholesale after sweep.
    /// Arrange: speculative block [A, B, C] (A=entry, C=terminator). All speculative.
    /// Act: sweep removes A, B, C.
    /// Assert: ContainingBlock is null for all. The block is not referenced by surviving nodes.
    /// </summary>
    [Fact]
    public void FullySpeculativeBlockRemovedWholesaleAfterSweep() {
        // Arrange: create 3 speculative NOP nodes and wire them into a block
        CfgInstruction a = CreateSpeculativeNode(new(0, 0x100));
        CfgInstruction b = CreateSpeculativeNode(new(0, 0x101));
        CfgInstruction c = CreateSpeculativeNode(new(0, 0x102));

        // Wire edges: A→B→C (straight-line)
        WireEdge(a, b);
        WireEdge(b, c);

        // Build the block manually (simulating what the linker would do)
        CfgBlock block = new(IdAllocator.AllocateId(), a);
        a.ContainingBlock = block;
        block.Append(b);
        b.ContainingBlock = block;
        block.Append(c);
        c.ContainingBlock = block;

        block.IsSpeculative.Should().BeTrue("all nodes are speculative");

        // Act
        _pruner.Sweep(a);

        // Assert
        a.ContainingBlock.Should().BeNull("swept node should have no block");
        b.ContainingBlock.Should().BeNull("swept node should have no block");
        c.ContainingBlock.Should().BeNull("swept node should have no block");
    }

    /// <summary>
    /// Mixed block truncated to observed prefix after sweep.
    /// Arrange: block [O1, O2, S1, S2] where O1, O2 observed and S1, S2 speculative.
    /// Act: sweep removes S1 and S2.
    /// Assert: block contains only [O1, O2]. Block is not speculative.
    /// </summary>
    [Fact]
    public void MixedBlockTruncatedToObservedPrefixAfterSweep() {
        // Arrange: observed prefix + speculative tail in one block
        CfgInstruction o1 = CreateObservedNode(new(0, 0x200));
        CfgInstruction o2 = CreateObservedNode(new(0, 0x201));
        CfgInstruction s1 = CreateSpeculativeNode(new(0, 0x202));
        CfgInstruction s2 = CreateSpeculativeNode(new(0, 0x203));

        // Wire chain: O1→O2→S1→S2
        WireEdge(o1, o2);
        WireEdge(o2, s1);
        WireEdge(s1, s2);

        // Build block containing all four
        CfgBlock block = new(IdAllocator.AllocateId(), o1);
        o1.ContainingBlock = block;
        block.Append(o2);
        o2.ContainingBlock = block;
        block.Append(s1);
        s1.ContainingBlock = block;
        block.Append(s2);
        s2.ContainingBlock = block;

        block.IsSpeculative.Should().BeTrue("block has speculative nodes");
        block.Instructions.Count.Should().Be(4);
        s1.ContainingBlock.Should().Be(block, "sanity: s1 is in block");

        // Act: sweep from S1
        HashSet<ICfgNode> removed = _pruner.Sweep(s1);

        // Assert
        removed.Should().Contain(s1, "S1 should be in the removed set");
        removed.Should().Contain(s2, "S2 should be in the removed set");
        block.Instructions.Count.Should().Be(2, "only observed prefix remains");
        block.Instructions[0].Should().Be(o1);
        block.Instructions[1].Should().Be(o2);
        block.Terminator.Should().Be(o2);
        block.IsSpeculative.Should().BeFalse("no speculative nodes remain");
        s1.ContainingBlock.Should().BeNull("swept nodes have no block");
        s2.ContainingBlock.Should().BeNull("swept nodes have no block");
    }

    /// <summary>
    /// Observed block losing a terminator successor edge keeps structure.
    /// Arrange: observed block [O1, O2, O3]. O3 has speculative successor S.
    /// Act: sweep removes S (calls RemoveEdge(O3, S)).
    /// Assert: block [O1, O2, O3] unchanged. O3.MaxSuccessorsCount unchanged.
    /// O3.CanHaveMoreSuccessors flipped back to true.
    /// </summary>
    [Fact]
    public void ObservedBlockLosingTerminatorSuccessorEdgeKeepsStructure() {
        // Arrange
        CfgInstruction o1 = CreateObservedNode(new(0, 0x300));
        CfgInstruction o2 = CreateObservedNode(new(0, 0x301));
        CfgInstruction o3 = CreateObservedNode(new(0, 0x302));
        CfgInstruction s = CreateSpeculativeNode(new(0, 0x310));

        // Build observed block
        CfgBlock block = new(IdAllocator.AllocateId(), o1);
        o1.ContainingBlock = block;
        block.Append(o2);
        o2.ContainingBlock = block;
        block.Append(o3);
        o3.ContainingBlock = block;

        // O3 is terminator with one speculative successor
        o3.MaxSuccessorsCount = 1;
        WireEdge(o3, s);
        o3.CanHaveMoreSuccessors.Should().BeFalse("cap is 1, one successor exists");

        // Act: sweep removes S
        _pruner.Sweep(s);

        // Assert: block unchanged
        block.Instructions.Count.Should().Be(3);
        block.Entry.Should().Be(o1);
        block.Terminator.Should().Be(o3);
        block.IsSpeculative.Should().BeFalse();

        // O3 had its successor removed
        o3.Successors.Should().NotContain(s);
        o3.MaxSuccessorsCount.Should().Be(1, "cap should not change");
        o3.CanHaveMoreSuccessors.Should().BeTrue("slot reopened after removal");
    }
}
