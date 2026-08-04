namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Exercises each successor-edge mutation path in <see cref="Spice86.Core.Emulator.CPU.CfgCpu.Linker.NodeLinker"/>
/// and asserts the node's successor bookkeeping stays internally consistent afterwards (see
/// <see cref="SuccessorConsistency"/>). These guard the drift surface where the per-type and
/// speculative-provenance sets must move in lockstep with <see cref="ICfgNode.Successors"/>.
/// </summary>
public sealed class SuccessorConsistencyTest : SpeculativeTestBase {
    private static readonly SegmentedAddress A = new(0, 0x100);
    private static readonly SegmentedAddress B = new(0, 0x200);
    private static readonly SegmentedAddress C = new(0, 0x300);

    [Fact]
    public void NewObservedEdgeIsConsistent() {
        CfgInstruction from = CreateObservedNode(A);
        CfgInstruction to = CreateObservedNode(B);

        NodeLinker.Link(InstructionSuccessorType.Normal, from, to);

        from.Successors.Should().Contain(to);
        SuccessorConsistency.AssertConsistent(from);
        SuccessorConsistency.AssertConsistent(to);
    }

    [Fact]
    public void EdgeToSpeculativeTargetRecordsProvenanceAndIsConsistent() {
        CfgInstruction from = CreateObservedNode(A);
        CfgInstruction speculativeTo = CreateSpeculativeNode(B);

        NodeLinker.Link(InstructionSuccessorType.Normal, from, speculativeTo);

        from.SpeculativelyWiredSuccessors.Should().Contain(speculativeTo);
        SuccessorConsistency.AssertConsistent(from);
    }

    [Fact]
    public void NewCallToReturnEdgeIsConsistent() {
        CfgInstruction from = CreateObservedNode(A);
        CfgInstruction to = CreateObservedNode(B);

        NodeLinker.Link(InstructionSuccessorType.CallToReturn, from, to);

        from.SuccessorsPerType[InstructionSuccessorType.CallToReturn].Should().Contain(to);
        SuccessorConsistency.AssertConsistent(from);
    }

    [Fact]
    public void ObservedRetraversalRetypesSpeculativeEdgeAndDropsProvenance() {
        CfgInstruction from = CreateObservedNode(A);
        CfgInstruction target = CreateSpeculativeNode(B);
        // Explorer wires a speculative Normal guess.
        NodeLinker.Link(InstructionSuccessorType.Normal, from, target);
        from.SpeculativelyWiredSuccessors.Should().Contain(target);

        // The target is later observed and re-reached via a different edge type.
        target.SetSpeculative(false);
        NodeLinker.Link(InstructionSuccessorType.CallToReturn, from, target);

        from.SuccessorsPerType[InstructionSuccessorType.CallToReturn].Should().Contain(target);
        from.SuccessorsPerType[InstructionSuccessorType.Normal].Should().NotContain(target);
        from.SpeculativelyWiredSuccessors.Should().BeEmpty();
        SuccessorConsistency.AssertConsistent(from);
    }

    [Fact]
    public void RemoveEdgeClearsAllMetadataAndIsConsistent() {
        CfgInstruction from = CreateObservedNode(A);
        CfgInstruction to = CreateSpeculativeNode(B);
        NodeLinker.Link(InstructionSuccessorType.Normal, from, to);

        NodeLinker.RemoveEdge(from, to);

        from.Successors.Should().NotContain(to);
        from.SpeculativelyWiredSuccessors.Should().NotContain(to);
        from.SuccessorsPerType.Values.SelectMany(bucket => bucket).Should().NotContain(to);
        to.Predecessors.Should().NotContain(from);
        SuccessorConsistency.AssertConsistent(from);
    }

    [Fact]
    public void ReplacingSpeculativelyWiredSuccessorMigratesProvenanceToNewTarget() {
        CfgInstruction predecessor = CreateObservedNode(A);
        CfgInstruction speculativeTarget = CreateSpeculativeNode(B);
        NodeLinker.Link(InstructionSuccessorType.Normal, predecessor, speculativeTarget);
        predecessor.SpeculativelyWiredSuccessors.Should().Contain(speculativeTarget);

        CfgInstruction observedTarget = CreateObservedNode(B);
        NodeLinker.ReplaceInstruction(speculativeTarget, observedTarget);

        predecessor.Successors.Should().Contain(observedTarget);
        predecessor.SpeculativelyWiredSuccessors.Should().NotContain(speculativeTarget);
        predecessor.SpeculativelyWiredSuccessors.Should().Contain(observedTarget);
        SuccessorConsistency.AssertConsistent(predecessor);
        SuccessorConsistency.AssertConsistent(observedTarget);
    }

    [Fact]
    public void SelectorNodeBetweenVariantsIsConsistent() {
        CfgInstruction predecessor = CreateObservedNode(A);
        // Two distinct signatures parsed at the same address (NOP then RET), giving two variants.
        WriteNop(B);
        CfgInstruction variantA = WriteNopAndParse(B);
        NodeIndex.Insert(variantA);
        WriteRet(B);
        CfgInstruction variantB = Parser.ParseInstructionAt(B);
        NodeIndex.Insert(variantB);

        NodeLinker.Link(InstructionSuccessorType.Normal, predecessor, variantA);
        SelectorNode selector = NodeLinker.CreateSelectorNodeBetween(variantA, variantB);

        selector.Successors.Should().Contain(variantA);
        selector.Successors.Should().Contain(variantB);
        SuccessorConsistency.AssertConsistent(selector);
        SuccessorConsistency.AssertConsistent(predecessor);
    }

    [Fact]
    public void ConsistencyCheckRejectsTypedSuccessorNotInSuccessorSet() {
        CfgInstruction node = CreateObservedNode(A);
        CfgInstruction orphan = CreateObservedNode(B);
        node.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { orphan };

        Action assert = () => SuccessorConsistency.AssertConsistent(node);

        assert.Should().Throw<Xunit.Sdk.XunitException>();
    }
}
