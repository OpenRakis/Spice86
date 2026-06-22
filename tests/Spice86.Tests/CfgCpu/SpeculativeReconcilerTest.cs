namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using Xunit;

/// <summary>
/// Dedicated unit tests for <see cref="SpeculativeReconciler"/>: the promote-or-sweep decision taken
/// when execution reaches a pre-existing speculative node and compares it against live memory.
/// </summary>
public sealed class SpeculativeReconcilerTest : SpeculativeTestBase {
    private readonly CurrentInstructions _currentInstructions;
    private readonly SpeculativeReconciler _reconciler;

    public SpeculativeReconcilerTest() {
        AddressReadWriteBreakpoints ioBreakpoints = new();
        EmulatorBreakpointsManager breakpointsManager =
            new(new PauseHandler(LoggerService), State, Memory, MemoryBreakpoints, ioBreakpoints);
        _currentInstructions = new CurrentInstructions(Memory, breakpointsManager, ReplacerRegistry);
        PreviousInstructions previousInstructions = new(Memory, ReplacerRegistry);
        SpeculativePromoter promoter = new(Compiler, _currentInstructions, previousInstructions);
        SpeculativeReachabilityPruner pruner = new(ReplacerRegistry);
        SignatureReducer signatureReducer = new(ReplacerRegistry);
        _reconciler = new SpeculativeReconciler(promoter, pruner, signatureReducer, NodeIndex);
    }

    /// <summary>
    /// When the speculative node still matches live memory, reconciliation promotes it in place and
    /// reports success; the address is not poisoned.
    /// </summary>
    [Fact]
    public void ReconcileMatchingSignaturePromotesInPlace() {
        SegmentedAddress address = new(0, 0x100);
        CfgInstruction speculative = CreateSpeculativeNode(address);
        // Live memory still holds the same NOP, so the live signature is equivalent.
        CfgInstruction live = Parser.ParseInstructionAt(address);

        bool promoted = _reconciler.Reconcile(speculative, live, address);

        promoted.Should().BeTrue("a matching speculative node is promoted, not swept");
        speculative.IsSpeculative.Should().BeFalse("promotion clears the speculative flag");
        _currentInstructions.GetAtAddress(address).Should().BeSameAs(speculative,
            "the promoted node is installed in the current cache");
        NodeIndex.PoisonSet.Should().NotContain(address, "a matching address must not be poisoned");
    }

    /// <summary>
    /// When live memory diverges from the speculative decode, reconciliation sweeps the node, poisons
    /// the address, and reports failure so the caller falls back to a fresh observed node.
    /// </summary>
    [Fact]
    public void ReconcileMismatchingSignatureSweepsAndPoisons() {
        SegmentedAddress address = new(0, 0x200);
        CfgInstruction speculative = CreateSpeculativeNode(address);
        // Rewrite memory so the live instruction is a RET, diverging from the speculative NOP.
        WriteRet(address);
        CfgInstruction live = Parser.ParseInstructionAt(address);

        bool promoted = _reconciler.Reconcile(speculative, live, address);

        promoted.Should().BeFalse("a diverged speculative node is swept, not promoted");
        NodeIndex.PoisonSet.Should().Contain(address, "a diverged address is poisoned to stop future speculation");
        NodeIndex.HasAddress(address).Should().BeFalse("the swept speculative node is removed from the index");
        _currentInstructions.GetAtAddress(address).Should().BeNull("a swept node must not be installed as current");
    }

    /// <summary>
    /// When live memory differs from the speculative decode only in a non-final field (a self-modified
    /// immediate), the opcode is unchanged, so the difference is reducible rather than a real conflict.
    /// Reconciliation merges the speculative node with the live instruction and promotes the survivor
    /// in place: the node stays in the graph, the address is not poisoned, and no sweep occurs.
    /// </summary>
    [Fact]
    public void ReconcileReducibleImmediateDiffReducesAndPromotes() {
        SegmentedAddress address = new(0, 0x300);
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        // Speculative decode of MOV AL, 0x11 (0xB0 0x11); the immediate is a non-final field.
        Memory.UInt8[physAddr] = 0xB0;
        Memory.UInt8[physAddr + 1] = 0x11;
        CfgInstruction speculative = Parser.ParseInstructionAt(address);
        speculative.SetSpeculative(true);
        NodeIndex.Insert(speculative);
        // Self-modify only the immediate: same opcode, so the final-field signatures still match.
        Memory.UInt8[physAddr + 1] = 0x22;
        CfgInstruction live = Parser.ParseInstructionAt(address);

        bool promoted = _reconciler.Reconcile(speculative, live, address);

        promoted.Should().BeTrue("a reducible same-opcode variant is merged and promoted, not swept");
        speculative.IsSpeculative.Should().BeFalse("the surviving reduced node is promoted to observed");
        NodeIndex.PoisonSet.Should().NotContain(address,
            "a reducible same-opcode variant must not poison the address");
        NodeIndex.HasAddress(address).Should().BeTrue("the reduced node stays in the graph");
        _currentInstructions.GetAtAddress(address).Should().BeSameAs(speculative,
            "the promoted survivor is installed in the current cache");
    }

    /// <summary>
    /// Reducing a self-modified immediate must preserve the speculatively-explored subgraph: a
    /// downstream speculative successor of the reconciled node stays wired and indexed rather than
    /// being discarded by a reachability sweep.
    /// </summary>
    [Fact]
    public void ReconcileReducibleImmediateDiffKeepsDownstreamSpeculativeSubgraph() {
        SegmentedAddress address = new(0, 0x400);
        SegmentedAddress successorAddress = new(0, 0x402);
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        Memory.UInt8[physAddr] = 0xB0;
        Memory.UInt8[physAddr + 1] = 0x11;
        CfgInstruction speculative = Parser.ParseInstructionAt(address);
        speculative.SetSpeculative(true);
        NodeIndex.Insert(speculative);
        CfgInstruction downstream = CreateSpeculativeNode(successorAddress);
        WireEdge(speculative, downstream);
        // Self-modify only the immediate: same opcode, reducible.
        Memory.UInt8[physAddr + 1] = 0x22;
        CfgInstruction live = Parser.ParseInstructionAt(address);

        bool promoted = _reconciler.Reconcile(speculative, live, address);

        promoted.Should().BeTrue();
        NodeIndex.HasAddress(successorAddress).Should().BeTrue(
            "the explored downstream speculative node must survive reduction, not be swept away");
        downstream.IsSpeculative.Should().BeTrue("downstream nodes stay speculative until visited");
    }
}
