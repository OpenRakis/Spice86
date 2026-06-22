namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

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
        SpeculativeReachabilityPruner pruner = new(NodeLinker, NodeIndex);
        _reconciler = new SpeculativeReconciler(promoter, pruner, NodeIndex);
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

        bool promoted = _reconciler.Reconcile(speculative, live.Signature, address);

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

        bool promoted = _reconciler.Reconcile(speculative, live.Signature, address);

        promoted.Should().BeFalse("a diverged speculative node is swept, not promoted");
        NodeIndex.PoisonSet.Should().Contain(address, "a diverged address is poisoned to stop future speculation");
        NodeIndex.HasAddress(address).Should().BeFalse("the swept speculative node is removed from the index");
        _currentInstructions.GetAtAddress(address).Should().BeNull("a swept node must not be installed as current");
    }
}
