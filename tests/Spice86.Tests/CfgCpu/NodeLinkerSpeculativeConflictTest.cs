namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Tests that resolving a successor conflict against a stale speculative node fully discards it
/// instead of only dropping the incoming edge, so the orphaned node and its exclusively-reachable
/// speculative subgraph do not linger in the index or the graph.
/// </summary>
public sealed class NodeLinkerSpeculativeConflictTest : SpeculativeTestBase {
    /// <summary>
    /// current -> speculative S is wired, S owns an exclusively-speculative child S2. When an observed
    /// node at the same address S is linked from current, the speculative node and its child must be
    /// swept from the index and fully detached, and current must point at the observed node.
    /// </summary>
    [Fact]
    public void LinkingObservedOverStaleSpeculativeSuccessorSweepsIt() {
        // Arrange
        SegmentedAddress currentAddress = new(0, 0x100);
        SegmentedAddress conflictAddress = new(0, 0x200);
        SegmentedAddress childAddress = new(0, 0x201);
        CfgInstruction current = CreateObservedNode(currentAddress);
        CfgInstruction speculativeSuccessor = CreateSpeculativeNode(conflictAddress);
        CfgInstruction speculativeChild = CreateSpeculativeNode(childAddress);
        WireEdge(speculativeSuccessor, speculativeChild);
        NodeLinker.Link(InstructionSuccessorType.Normal, current, speculativeSuccessor);

        CfgInstruction observedSuccessor = CreateObservedNode(conflictAddress);

        // Act
        NodeLinker.Link(InstructionSuccessorType.Normal, current, observedSuccessor);

        // Assert: the stale speculative node and its exclusively-reachable child are gone.
        NodeIndex.Contains(speculativeSuccessor).Should().BeFalse("the stale speculative successor must be swept, not just unlinked");
        NodeIndex.Contains(speculativeChild).Should().BeFalse("the child reachable only through the swept node must go too");
        speculativeSuccessor.Successors.Should().BeEmpty();
        speculativeSuccessor.Predecessors.Should().BeEmpty();
        speculativeChild.Successors.Should().BeEmpty();
        speculativeChild.Predecessors.Should().BeEmpty();

        // Assert: current now points at the observed node instead of the swept speculative one.
        current.Successors.Should().Contain(observedSuccessor);
        current.Successors.Should().NotContain(speculativeSuccessor);
        current.SuccessorsPerAddress.Should().ContainKey(conflictAddress);
        current.SuccessorsPerAddress[conflictAddress].Should().Be(observedSuccessor);
    }
}
