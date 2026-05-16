namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying;
using Spice86.Shared.Emulator.Memory;

using static CfgTestHelpers;

using Xunit;

/// <summary>
/// Tests how <see cref="NodeLinker.CreateSelectorNodeBetween"/> places selector nodes into blocks.
/// </summary>
public class SelectorBlockAssignmentTest {
    private const ushort PredecessorSegment = 0x1000;
    private const ushort VariantSegment = 0x2000;

    [Fact]
    public void Selector_BetweenNonTerminatorAndSameAddressVariant_RemainsInPredecessorBlock() {
        using LinkerHarness harness = new();
        SegmentedAddress aAddr = new(PredecessorSegment, 0x0000);
        SegmentedAddress variantAddr = new(PredecessorSegment, 0x0001);
        CfgInstruction a = harness.CreateInstruction(aAddr, 0x90, 1, InstructionKind.None);
        CfgInstruction variantA = harness.CreateInstruction(variantAddr, 0x91, 1, InstructionKind.None);
        harness.Linker.Link(InstructionSuccessorType.Normal, a, variantA);

        CfgBlock alpha = GetContainingBlock(a);
        alpha.Instructions.Should().HaveCount(2);
        alpha.Entry.Should().BeSameAs(a);
        alpha.Terminator.Should().BeSameAs(variantA);

        CfgInstruction variantB = harness.CreateInstruction(variantAddr, 0x92, 1, InstructionKind.None);

        SelectorNode selector = harness.Linker.CreateSelectorNodeBetween(variantA, variantB);

        CfgBlock alphaAfter = GetContainingBlock(a);
        CfgBlock selectorBlock = GetContainingBlock(selector);
        alphaAfter.Instructions.Should().Equal([a, selector],
            "an adjacent non-terminator predecessor can append the selector by normal continuation");
        alphaAfter.Entry.Should().BeSameAs(a);
        alphaAfter.Terminator.Should().BeSameAs(selector);
        selectorBlock.Should().BeSameAs(alphaAfter,
            "being returned for execution is not itself a graph boundary");
        selectorBlock.Terminator.Should().BeSameAs(selector);

        variantA.ContainingBlock.Should().NotBeSameAs(alphaAfter);
        variantB.ContainingBlock.Should().NotBeSameAs(alphaAfter);
    }

    /// <summary>
    /// Predecessor is a <strong>terminator</strong> (e.g. a CALL). The selector's
    /// predecessor wiring goes through the boundary path (continuation cannot fire because the
    /// predecessor is itself a block terminator); the selector must therefore land in its own
    /// one-node block, not be silently dropped.
    ///
    /// <para>
    /// This is the <c>selfmodifycall.bin</c> regression: a <c>call</c> at <c>F000:000D</c> targets
    /// the handler at <c>F000:0017</c>, and the patched return-point at <c>F000:0010</c> ends up
    /// guarded by a SelectorNode dispatching between <c>nop</c> and <c>hlt</c>. The call's
    /// <c>NextInMemoryAddress</c> matches the selector's address but the call IS a block terminator,
    /// so Case T cannot fire and <c>AssignBlockForNext</c> must open a block for the selector.
    /// </para>
    /// </summary>
    [Fact]
    public void Selector_PredecessorIsCallTerminator_LandsInOwnOneNodeBlock() {
        using LinkerHarness harness = new();
        SegmentedAddress callAddr = new(PredecessorSegment, 0x0000);
        SegmentedAddress variantAddr = new(PredecessorSegment, 0x0003);

        // Predecessor is a call (terminator) with NextInMemoryAddress matching the variant address.
        CfgInstruction call = harness.CreateInstruction(callAddr, 0xE8, 3, InstructionKind.Call);
        CfgInstruction variantA = harness.CreateInstruction(variantAddr, 0x90, 1, InstructionKind.None);
        harness.Linker.Link(InstructionSuccessorType.Normal, call, variantA);

        CfgBlock callBlock = GetContainingBlock(call);
        callBlock.Instructions.Should().HaveCount(1,
            "the call is a block terminator, so its block contains only itself");
        callBlock.Terminator.Should().BeSameAs(call);
        callBlock.IsDiscoveryComplete.Should().BeTrue();

        CfgInstruction variantB = harness.CreateInstruction(variantAddr, 0x91, 1, InstructionKind.None);

        SelectorNode selector = harness.Linker.CreateSelectorNodeBetween(variantA, variantB);

        CfgBlock selectorBlock = GetContainingBlock(selector);
        selectorBlock.Should().NotBeSameAs(callBlock,
            "the selector cannot be appended to a block whose terminator is a call (Case T does not fire)");
        selectorBlock.Instructions.Should().HaveCount(1,
            "the selector's block contains only the selector");
        selectorBlock.Entry.Should().BeSameAs(selector);
        selectorBlock.Terminator.Should().BeSameAs(selector);
        selectorBlock.IsDiscoveryComplete.Should().BeTrue(
            "a block terminator entry auto-closes its block on creation");

        callBlock.Instructions.Should().HaveCount(1);
        callBlock.Terminator.Should().BeSameAs(call);
    }

    /// <summary>
    /// Predecessor is a RET terminator. Same expectation as a CALL: the selector lands
    /// in its own one-node block.
    /// </summary>
    [Fact]
    public void Selector_PredecessorIsReturnTerminator_LandsInOwnOneNodeBlock() {
        using LinkerHarness harness = new();
        SegmentedAddress retAddr = new(PredecessorSegment, 0x0000);
        SegmentedAddress variantAddr = new(PredecessorSegment, 0x0001);

        CfgInstruction ret = harness.CreateInstruction(retAddr, 0xC3, 1, InstructionKind.Return);
        CfgInstruction variantA = harness.CreateInstruction(variantAddr, 0x90, 1, InstructionKind.None);
        harness.Linker.Link(InstructionSuccessorType.Normal, ret, variantA);

        CfgInstruction variantB = harness.CreateInstruction(variantAddr, 0x91, 1, InstructionKind.None);

        SelectorNode selector = harness.Linker.CreateSelectorNodeBetween(variantA, variantB);

        CfgBlock selectorBlock = GetContainingBlock(selector);
        selectorBlock.Should().NotBeSameAs(ret.ContainingBlock,
            "the selector cannot be appended to a ret-terminated block");
        selectorBlock.Instructions.Should().HaveCount(1);
        selectorBlock.Terminator.Should().BeSameAs(selector);
    }

    /// <summary>
    /// Predecessor is an unconditional JMP terminator. Same expectation: selector lands
    /// in its own one-node block.
    /// </summary>
    [Fact]
    public void Selector_PredecessorIsJumpTerminator_LandsInOwnOneNodeBlock() {
        using LinkerHarness harness = new();
        SegmentedAddress jmpAddr = new(PredecessorSegment, 0x0000);
        SegmentedAddress variantAddr = new(PredecessorSegment, 0x0002);

        CfgInstruction jmp = harness.CreateInstruction(jmpAddr, 0xEB, 2, InstructionKind.Jump);
        CfgInstruction variantA = harness.CreateInstruction(variantAddr, 0x90, 1, InstructionKind.None);
        harness.Linker.Link(InstructionSuccessorType.Normal, jmp, variantA);

        CfgInstruction variantB = harness.CreateInstruction(variantAddr, 0x91, 1, InstructionKind.None);

        SelectorNode selector = harness.Linker.CreateSelectorNodeBetween(variantA, variantB);

        CfgBlock selectorBlock = GetContainingBlock(selector);
        selectorBlock.Should().NotBeSameAs(jmp.ContainingBlock);
        selectorBlock.Instructions.Should().HaveCount(1);
        selectorBlock.Terminator.Should().BeSameAs(selector);
    }

    /// <summary>
    /// Predecessor is a non-terminator whose <c>NextInMemoryAddress</c> does NOT match
    /// the variant's address (e.g. a far-jump landing back into the variant's address). Continuation
    /// cannot fire because the address adjacency check fails; the selector must land in its own block.
    /// </summary>
    [Fact]
    public void Selector_PredecessorIsNonAdjacent_LandsInOwnOneNodeBlock() {
        using LinkerHarness harness = new();
        SegmentedAddress predAddr = new(PredecessorSegment, 0x0000);
        SegmentedAddress variantAddr = new(VariantSegment, 0x0000); // different segment, not adjacent

        CfgInstruction pred = harness.CreateInstruction(predAddr, 0x90, 1, InstructionKind.None);
        CfgInstruction variantA = harness.CreateInstruction(variantAddr, 0x91, 1, InstructionKind.None);
        harness.Linker.Link(InstructionSuccessorType.Normal, pred, variantA);

        // pred is non-terminator; its block is just [pred] in-discovery (pred's NextInMemoryAddress
        // is PredecessorSegment:0x0001, NOT variantAddr, so cold-path continuation did not fold variantA in).
        CfgBlock predBlock = GetContainingBlock(pred);
        predBlock.Instructions.Should().ContainSingle().Which.Should().BeSameAs(pred,
            "non-adjacent predecessor stays alone in its block (continuation fails on the address check)");

        CfgInstruction variantB = harness.CreateInstruction(variantAddr, 0x92, 1, InstructionKind.None);

        SelectorNode selector = harness.Linker.CreateSelectorNodeBetween(variantA, variantB);

        CfgBlock selectorBlock = GetContainingBlock(selector);
        selectorBlock.Should().NotBeSameAs(predBlock,
            "non-adjacent address means continuation cannot fire, so the selector lands in its own block");
        selectorBlock.Instructions.Should().HaveCount(1);
        selectorBlock.Terminator.Should().BeSameAs(selector);
    }

    /// <summary>
    /// Multiple predecessors all flowing into the same variant address. Only one of
    /// them needs to absorb the selector via continuation; the rest land in the boundary path.
    /// All predecessors and the selector must end up with non-null ContainingBlock.
    /// </summary>
    [Fact]
    public void Selector_MultiplePredecessors_RoutesBothPredecessorsThroughSelector() {
        using LinkerHarness harness = new();

        SegmentedAddress pred1Addr = new(PredecessorSegment, 0x0000);
        SegmentedAddress variantAddr = new(PredecessorSegment, 0x0001);
        SegmentedAddress pred2Addr = new(VariantSegment, 0x0000);

        CfgInstruction pred1 = harness.CreateInstruction(pred1Addr, 0x90, 1, InstructionKind.None);
        CfgInstruction pred2 = harness.CreateInstruction(pred2Addr, 0x91, 1, InstructionKind.None);
        CfgInstruction variantA = harness.CreateInstruction(variantAddr, 0xA0, 1, InstructionKind.None);

        harness.Linker.Link(InstructionSuccessorType.Normal, pred1, variantA);
        harness.Linker.Link(InstructionSuccessorType.Normal, pred2, variantA);

        variantA.Predecessors.Should().Contain(new ICfgNode[] { pred1, pred2 });

        CfgInstruction variantB = harness.CreateInstruction(variantAddr, 0xA1, 1, InstructionKind.None);

        SelectorNode selector = harness.Linker.CreateSelectorNodeBetween(variantA, variantB);

        GetContainingBlock(selector).Successors.Should().Contain(new ICfgNode[] { variantA, variantB });
        pred1.Successors.Should().Contain(selector);
        pred2.Successors.Should().Contain(selector);
    }

    /// <summary>
    /// Full <c>selfmodifycall</c>-shape regression at the linker level: the variant's
    /// predecessor is a CALL, and the selector dispatches between two variants. Asserts the exact
    /// shape we want for that binary's CFG: call's block is finalised at the call, the selector
    /// has its own one-node block, both variants live in their own discovery-complete blocks.
    /// </summary>
    [Fact]
    public void Selector_BetweenCallTerminatorAndTwoVariants_ProducesValidSelfModifyCallShape() {
        using LinkerHarness harness = new();
        SegmentedAddress callAddr = new(PredecessorSegment, 0x0000);
        SegmentedAddress variantAddr = new(PredecessorSegment, 0x0003);

        CfgInstruction call = harness.CreateInstruction(callAddr, 0xE8, 3, InstructionKind.Call);
        CfgInstruction nop = harness.CreateInstruction(variantAddr, 0x90, 1, InstructionKind.None);   // first iteration variant
        CfgInstruction hlt = harness.CreateInstruction(variantAddr, 0xF4, 1, InstructionKind.None);   // patched second iteration variant

        harness.Linker.Link(InstructionSuccessorType.Normal, call, nop);

        SelectorNode selector = harness.Linker.CreateSelectorNodeBetween(nop, hlt);

        CfgBlock callBlock = GetContainingBlock(call);
        callBlock.Instructions.Should().ContainSingle().Which.Should().BeSameAs(call);
        callBlock.IsDiscoveryComplete.Should().BeTrue();

        CfgBlock selectorBlock = GetContainingBlock(selector);
        selectorBlock.Instructions.Should().ContainSingle().Which.Should().BeSameAs(selector);
        selectorBlock.IsDiscoveryComplete.Should().BeTrue();

        nop.ContainingBlock.Should().NotBeSameAs(selectorBlock);
        hlt.ContainingBlock.Should().NotBeSameAs(selectorBlock);

        callBlock.Successors.Should().Contain(selector);
        selectorBlock.Successors.Should().Contain(new ICfgNode[] { nop, hlt });
    }

    /// <summary>
    /// Order independence of <see cref="NodeLinker.CreateSelectorNodeBetween"/>.
    /// The caller may pass the two variants in either order; the resulting block layout must
    /// be the same. This is the contract that frees downstream block-reconciliation code from
    /// having to special-case "selector with no predecessors yet" via node-type checks.
    /// </summary>
    [Fact]
    public void Selector_CreateSelectorNodeBetween_IsOrderIndependent() {
        // Set up two identical CFGs and inject the selector with the variants in opposite order.
        SegmentedAddress predAddr = new(PredecessorSegment, 0x0000);
        SegmentedAddress variantAddr = new(PredecessorSegment, 0x0001);

        CfgBlock RunInjection(bool variantWithPredecessorFirst) {
            using LinkerHarness harness = new();
            CfgInstruction pred = harness.CreateInstruction(predAddr, 0x90, 1, InstructionKind.None);
            CfgInstruction variantInGraph = harness.CreateInstruction(variantAddr, 0xA0, 1, InstructionKind.None);
            harness.Linker.Link(InstructionSuccessorType.Normal, pred, variantInGraph);

            CfgInstruction freshVariant = harness.CreateInstruction(variantAddr, 0xA1, 1, InstructionKind.None);
            SelectorNode selector = variantWithPredecessorFirst
                ? harness.Linker.CreateSelectorNodeBetween(variantInGraph, freshVariant)
                : harness.Linker.CreateSelectorNodeBetween(freshVariant, variantInGraph);

            return GetContainingBlock(selector);
        }

        CfgBlock blockOrderA = RunInjection(variantWithPredecessorFirst: true);
        CfgBlock blockOrderB = RunInjection(variantWithPredecessorFirst: false);

        // The two runs are independent harness instances (so block ids differ), but the
        // structural shape of the result must be the same: a discovery-complete block
        // terminating at the selector.
        blockOrderA.Instructions.Should().HaveCount(blockOrderB.Instructions.Count,
            "the selector's containing block has the same structure regardless of argument order");
        blockOrderA.IsDiscoveryComplete.Should().Be(blockOrderB.IsDiscoveryComplete);
        blockOrderA.Terminator.GetType().Should().Be(blockOrderB.Terminator.GetType());
    }

}
