namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// Tests for the Speculative Reachability Pruner.
/// These validate that the sweep correctly removes speculative nodes and maintains graph integrity.
/// </summary>
public sealed class SpeculativeReachabilityPrunerTest : SpeculativeTestBase {
    private readonly SpeculativeReachabilityPruner _pruner;

    public SpeculativeReachabilityPrunerTest() {
        _pruner = new SpeculativeReachabilityPruner(ReplacerRegistry);
    }

    /// <summary>
    /// Sweep removes full speculative chain.
    /// Arrange: speculative chain A->B->C (with proper edges). No observed predecessors.
    /// Act: Sweep(A).
    /// Assert: all three removed from the index. All edges detached.
    /// </summary>
    [Fact]
    public void SweepRemovesFullSpeculativeChain() {
        // Arrange
        CfgInstruction a = CreateSpeculativeNode(new(0, 0x100));
        CfgInstruction b = CreateSpeculativeNode(new(0, 0x101));
        CfgInstruction c = CreateSpeculativeNode(new(0, 0x102));
        WireEdge(a, b);
        WireEdge(b, c);

        // Act
        HashSet<ICfgNode> removed = _pruner.Sweep(a);

        // Assert
        removed.Should().Contain(a);
        removed.Should().Contain(b);
        removed.Should().Contain(c);
        NodeIndex.HasAddress(new(0, 0x100)).Should().BeFalse();
        NodeIndex.HasAddress(new(0, 0x101)).Should().BeFalse();
        NodeIndex.HasAddress(new(0, 0x102)).Should().BeFalse();
        a.Successors.Should().BeEmpty();
        a.Predecessors.Should().BeEmpty();
        b.Successors.Should().BeEmpty();
        b.Predecessors.Should().BeEmpty();
        c.Successors.Should().BeEmpty();
        c.Predecessors.Should().BeEmpty();
    }

    /// <summary>
    /// Sweep preserves survivors reachable from another observed root.
    /// Arrange: speculative chain A->B->C. Observed node O also has a successor edge to B.
    /// Act: Sweep(A).
    /// Assert: A removed. B and C survive (reachable from O).
    /// </summary>
    [Fact]
    public void SweepPreservesSurvivorsReachableFromObservedRoot() {
        // Arrange
        CfgInstruction a = CreateSpeculativeNode(new(0, 0x200));
        CfgInstruction b = CreateSpeculativeNode(new(0, 0x201));
        CfgInstruction c = CreateSpeculativeNode(new(0, 0x202));
        CfgInstruction o = CreateObservedNode(new(0, 0x210));
        WireEdge(a, b);
        WireEdge(b, c);
        WireEdge(o, b);

        // Act
        HashSet<ICfgNode> removed = _pruner.Sweep(a);

        // Assert
        removed.Should().Contain(a);
        removed.Should().NotContain(b, "B is reachable from observed O");
        removed.Should().NotContain(c, "C is reachable from observed O via B");
        NodeIndex.HasAddress(new(0, 0x200)).Should().BeFalse();
        NodeIndex.HasAddress(new(0, 0x201)).Should().BeTrue("B survives");
        NodeIndex.HasAddress(new(0, 0x202)).Should().BeTrue("C survives");
        b.Predecessors.Should().Contain(o, "B still has predecessor from O");
    }

    /// <summary>
    /// Sweep handles a cycle in the speculative region.
    /// Arrange: speculative nodes A->B->C->B (cycle). No external observed predecessor.
    /// Act: Sweep(A).
    /// Assert: A, B, C all removed.
    /// </summary>
    [Fact]
    public void SweepHandlesCycleInSpeculativeRegion() {
        // Arrange
        CfgInstruction a = CreateSpeculativeNode(new(0, 0x300));
        CfgInstruction b = CreateSpeculativeNode(new(0, 0x301));
        CfgInstruction c = CreateSpeculativeNode(new(0, 0x302));
        WireEdge(a, b);
        WireEdge(b, c);
        WireEdge(c, b); // cycle

        // Act
        HashSet<ICfgNode> removed = _pruner.Sweep(a);

        // Assert
        removed.Should().Contain(a);
        removed.Should().Contain(b);
        removed.Should().Contain(c);
        NodeIndex.HasAddress(new(0, 0x300)).Should().BeFalse();
        NodeIndex.HasAddress(new(0, 0x301)).Should().BeFalse();
        NodeIndex.HasAddress(new(0, 0x302)).Should().BeFalse();
    }

    /// <summary>
    /// Sweep handles a diamond (join) in the speculative region.
    /// Arrange: A->B, A->C, B->D, C->D (diamond). All speculative.
    /// Act: Sweep(A).
    /// Assert: A, B, C, D all removed.
    /// </summary>
    [Fact]
    public void SweepHandlesDiamondInSpeculativeRegion() {
        // Arrange
        CfgInstruction a = CreateSpeculativeNode(new(0, 0x400));
        CfgInstruction b = CreateSpeculativeNode(new(0, 0x401));
        CfgInstruction c = CreateSpeculativeNode(new(0, 0x402));
        CfgInstruction d = CreateSpeculativeNode(new(0, 0x403));
        WireEdge(a, b);
        WireEdge(a, c);
        WireEdge(b, d);
        WireEdge(c, d);

        // Act
        HashSet<ICfgNode> removed = _pruner.Sweep(a);

        // Assert: all four nodes are in the removed set
        removed.Should().Contain(a);
        removed.Should().Contain(b);
        removed.Should().Contain(c);
        removed.Should().Contain(d);

        // ...gone from the index
        NodeIndex.HasAddress(new(0, 0x400)).Should().BeFalse();
        NodeIndex.HasAddress(new(0, 0x401)).Should().BeFalse();
        NodeIndex.HasAddress(new(0, 0x402)).Should().BeFalse();
        NodeIndex.HasAddress(new(0, 0x403)).Should().BeFalse();

        // ...and every edge detached, including the diamond's join node D which had two predecessors.
        a.Successors.Should().BeEmpty();
        a.Predecessors.Should().BeEmpty();
        b.Successors.Should().BeEmpty();
        b.Predecessors.Should().BeEmpty();
        c.Successors.Should().BeEmpty();
        c.Predecessors.Should().BeEmpty();
        d.Successors.Should().BeEmpty();
        d.Predecessors.Should().BeEmpty();
    }

    /// <summary>
    /// RemoveEdge refreshes SuccessorInvariant on predecessor.
    /// Arrange: observed conditional P with MaxSuccessorsCount=2, two successors A and B.
    /// Act: RemoveEdge(P, A).
    /// Assert: P.CanHaveMoreSuccessors == true. SuccessorsPerAddress no longer references A.
    /// </summary>
    [Fact]
    public void RemoveEdgeRefreshesSuccessorInvariantOnPredecessor() {
        // Arrange: observed P with two successors
        CfgInstruction p = CreateObservedNode(new(0, 0x500));
        CfgInstruction a = CreateSpeculativeNode(new(0, 0x510));
        CfgInstruction b = CreateSpeculativeNode(new(0, 0x520));
        p.MaxSuccessorsCount = 2;
        WireEdge(p, a);
        WireEdge(p, b);
        p.CanHaveMoreSuccessors.Should().BeFalse("cap is 2 and 2 successors exist");

        // Act
        NodeLinker.RemoveEdge(p, a);

        // Assert
        p.CanHaveMoreSuccessors.Should().BeTrue("removed one successor, reopened slot");
        p.Successors.Should().NotContain(a);
        p.Successors.Should().Contain(b);
        p.SuccessorsPerAddress.Should().NotContainKey(a.Address);
        p.SuccessorsPerAddress.Should().ContainKey(b.Address);
    }

    /// <summary>
    /// After RemoveEdge, a new link through the reopened slot succeeds.
    /// </summary>
    [Fact]
    public void AfterRemoveEdgeNewLinkThroughReopenedSlotSucceeds() {
        // Arrange
        CfgInstruction p = CreateObservedNode(new(0, 0x600));
        CfgInstruction a = CreateSpeculativeNode(new(0, 0x610));
        CfgInstruction b = CreateSpeculativeNode(new(0, 0x620));
        p.MaxSuccessorsCount = 2;
        WireEdge(p, a);
        WireEdge(p, b);

        // Remove A to reopen a slot
        NodeLinker.RemoveEdge(p, a);
        p.CanHaveMoreSuccessors.Should().BeTrue();

        // Act: link a new node through the reopened slot
        CfgInstruction newNode = CreateObservedNode(new(0, 0x630));
        WireEdge(p, newNode);

        // Assert
        p.Successors.Should().Contain(b);
        p.Successors.Should().Contain(newNode);
        p.Successors.Count.Should().Be(2);
        p.CanHaveMoreSuccessors.Should().BeFalse("cap is 2 and 2 successors exist again");
    }
}
