namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.Exceptions;
using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Logging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;

using Xunit;

/// <summary>
/// Tests for provenance-aware linker/selector safety and speculative promotion correctness.
/// </summary>
public sealed class SpeculativeLinkerAndPromotionTest : SpeculativeTestBase {
    private readonly SpeculativePromoter _promoter;
    private readonly CurrentInstructions _currentInstructions;
    private readonly PreviousInstructions _previousInstructions;

    public SpeculativeLinkerAndPromotionTest() {
        AddressReadWriteBreakpoints memoryBreakpoints = new();
        AddressReadWriteBreakpoints ioBreakpoints = new();
        EmulatorBreakpointsManager breakpointsManager = new(new PauseHandler(LoggerService), State, Memory, memoryBreakpoints, ioBreakpoints);
        _currentInstructions = new CurrentInstructions(Memory, breakpointsManager, ReplacerRegistry);
        _previousInstructions = new PreviousInstructions(Memory, ReplacerRegistry);
        _promoter = new SpeculativePromoter(Compiler, _currentInstructions, _previousInstructions);
    }

    /// <summary>
    /// ResolveSuccessorConflict with existing speculative node discards it, no selector.
    /// Arrange: observed node P has a speculative successor S at address X.
    /// A fresh observed node N arrives at address X.
    /// Act: Link(Normal, P, N) triggers conflict resolution.
    /// Assert: S removed from P's successors. N linked. No SelectorNode created.
    /// </summary>
    [Fact]
    public void ResolveSuccessorConflictWithExistingSpeculativeDiscardsNoSelector() {
        // Arrange
        SegmentedAddress addrP = new(0, 0x100);
        SegmentedAddress addrX = new(0, 0x110);

        CfgInstruction p = CreateObservedNode(addrP);
        CfgInstruction s = CreateSpeculativeNode(addrX);
        p.MaxSuccessorsCount = 2;
        WireEdge(p, s);

        // Create a fresh observed node at the same address X
        CfgInstruction n = CreateObservedNode(addrX);

        // Act: link P -> N should resolve conflict by discarding S
        ICfgNode resolved = NodeLinker.Link(InstructionSuccessorType.Normal, p, n);

        // Assert
        resolved.Should().Be(n, "the fresh observed node should win");
        p.Successors.Should().Contain(n);
        p.Successors.Should().NotContain(s, "existing speculative node should be discarded");
        // No selector should exist in successors
        p.Successors.OfType<Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode>()
            .Should().BeEmpty("no selector should be created for speculative conflict");
    }

    /// <summary>
    /// ResolveSuccessorConflict with an existing speculative node must preserve the caller's
    /// successor type instead of hardcoding <see cref="InstructionSuccessorType.Normal"/>.
    ///
    /// Models a seeded known-safe handler whose CALL eagerly wired a speculative
    /// <see cref="InstructionSuccessorType.CallToReturn"/> continuation edge. When the real
    /// continuation node later replaces that speculative node, the replacement edge must stay
    /// classified as CallToReturn so the partitioner/generator see the same call-return metadata as
    /// the non-speculative graph. Hardcoding Normal silently diverges the two graphs.
    /// </summary>
    [Fact]
    public void ResolveSuccessorConflictWithExistingSpeculativePreservesCallToReturnType() {
        // Arrange: observed call-site P with a speculative CallToReturn continuation S at address X.
        SegmentedAddress addrP = new(0, 0x120);
        SegmentedAddress addrX = new(0, 0x130);

        CfgInstruction p = CreateObservedNode(addrP);
        CfgInstruction s = CreateSpeculativeNode(addrX);
        p.MaxSuccessorsCount = 2;
        WireEdge(p, s);

        // A fresh observed continuation node arrives at the same address X.
        CfgInstruction n = CreateObservedNode(addrX);

        // Act: link the real CallToReturn continuation, replacing the existing speculative node.
        ICfgNode resolved = NodeLinker.Link(InstructionSuccessorType.CallToReturn, p, n);

        // Assert: the replacement edge keeps its CallToReturn classification, not Normal.
        resolved.Should().Be(n, "the fresh observed node should win");
        p.Successors.Should().Contain(n);
        p.Successors.Should().NotContain(s, "the existing speculative node should be discarded");
        p.SuccessorsPerType.Should().ContainKey(InstructionSuccessorType.CallToReturn,
            "the replacement edge must keep the caller's successor type, not be reclassified as Normal");
        p.SuccessorsPerType[InstructionSuccessorType.CallToReturn].Should().Contain(n);
        p.SuccessorsPerType.Should().NotContainKey(InstructionSuccessorType.Normal,
            "the replacement must not be reclassified as Normal");
    }

    /// <summary>
    /// CreateSelectorNodeBetween throws if either operand is speculative.
    /// </summary>
    [Fact]
    public void CreateSelectorNodeBetweenThrowsIfEitherOperandIsSpeculative() {
        // Arrange
        CfgInstruction observed = CreateObservedNode(new(0, 0x200));
        CfgInstruction speculative = CreateSpeculativeNode(new(0, 0x200));

        // Act & Assert
        Action act = () => NodeLinker.CreateSelectorNodeBetween(speculative, observed);
        act.Should().Throw<UnhandledCfgDiscrepancyException>();

        Action act2 = () => NodeLinker.CreateSelectorNodeBetween(observed, speculative);
        act2.Should().Throw<UnhandledCfgDiscrepancyException>();
    }

    /// <summary>
    /// Promote-on-match flips node to observed and installs in caches.
    /// Arrange: speculative node S at address A matching current memory bytes.
    /// Act: Promote(S).
    /// Assert: S.IsSpeculative == false. S.IsLive == true. Caches contain S.
    /// </summary>
    [Fact]
    public void PromoteOnMatchFlipsNodeToObservedAndInstallsInCaches() {
        // Arrange
        SegmentedAddress addr = new(0, 0x300);
        CfgInstruction s = CreateSpeculativeNode(addr);
        s.IsSpeculative.Should().BeTrue();
        s.IsLive.Should().BeFalse("speculative nodes are non-live");

        // Act
        _promoter.Promote(s);

        // Assert
        s.IsSpeculative.Should().BeFalse("promotion clears speculative flag");
        s.IsLive.Should().BeTrue("promoted node should be live");
        CfgInstruction? fromCurrent = _currentInstructions.GetAtAddress(addr);
        fromCurrent.Should().Be(s, "promoted node should be in CurrentInstructions");
    }

    /// <summary>
    /// Cold-path promotion: first execution at a speculated address promotes in-place.
    /// Uses the InstructionsFeeder flow.
    /// </summary>
    [Fact]
    public void ColdPathPromotionFirstExecutionAtSpeculatedAddressPromotes() {
        InstructionsFeeder feeder = CreateSpeculativeFeeder();

        // NOP at A falling through to a RET at B (so exploration terminates).
        SegmentedAddress addrA = new(0, 0x400);
        SegmentedAddress addrB = new(0, 0x401);
        WriteNop(addrA);
        WriteRet(addrB);

        // First: parse A as observed (triggers exploration of B as speculative)
        CfgInstruction observedA = feeder.GetInstructionFromMemory(addrA);
        observedA.IsSpeculative.Should().BeFalse();

        // Verify B is speculative in the index
        CfgInstruction speculativeB = feeder.NodeIndex.GetAtAddress(addrB).FirstOrDefault()
            ?? throw new InvalidOperationException("address B should hold a speculative node after exploration");
        speculativeB.IsSpeculative.Should().BeTrue();

        // Act: now "execute" at address B (cold-path promotion)
        CfgInstruction result = feeder.GetInstructionFromMemory(addrB);

        // Assert: the speculative node was promoted (same instance, now observed)
        result.Should().Be(speculativeB, "promoted node should be returned");
        result.IsSpeculative.Should().BeFalse("node should be promoted to observed");
    }

    /// <summary>
    /// Cold-path mismatch: discard + poison.
    /// When memory at a speculative address differs from what was decoded,
    /// the speculative node is removed, the address is poisoned, and a fresh observed node is returned.
    /// </summary>
    [Fact]
    public void ColdPathMismatchDiscardsAndPoisons() {
        InstructionsFeeder feeder = CreateSpeculativeFeeder();

        // NOP at A falling through to a NOP at B (exploration decodes the NOP at B).
        SegmentedAddress addrA = new(0, 0x500);
        SegmentedAddress addrB = new(0, 0x501);
        WriteNop(addrA);
        WriteNop(addrB);

        // Parse A (triggers exploration -> B decoded as speculative NOP)
        feeder.GetInstructionFromMemory(addrA);
        CfgInstruction speculativeB = feeder.NodeIndex.GetAtAddress(addrB).FirstOrDefault()
            ?? throw new InvalidOperationException("address B should hold a speculative node after exploration");
        speculativeB.IsSpeculative.Should().BeTrue();

        // Now change memory at B (simulating SMC or decode-into-data)
        WriteRet(addrB); // change to RET

        // Act: cold-path at B should detect mismatch, discard, poison
        CfgInstruction result = feeder.GetInstructionFromMemory(addrB);

        // Assert
        result.Should().NotBe(speculativeB, "mismatch should return a fresh node");
        result.IsSpeculative.Should().BeFalse("fresh node should be observed");
        feeder.NodeIndex.PoisonSet.Should().Contain(addrB, "mismatched address should be poisoned");
    }

    /// <summary>
    /// Cold-path with multiple self-modified speculative variants at one address: reconciliation must
    /// promote the variant whose final signature matches live memory, not an arbitrary sibling.
    /// Self-modifying code lets the explorer mint distinct variants (different opcodes) at the same
    /// address. When execution reaches that address, picking a non-matching sibling would sweep it and
    /// permanently poison the address even though a sibling matched memory.
    /// </summary>
    [Fact]
    public void ColdPathPromotesSpeculativeVariantMatchingLiveMemoryAmongSiblings() {
        InstructionsFeeder feeder = CreateSpeculativeFeeder();

        // Two jump sites both target B. B initially decodes to a NOP falling through to a RET.
        SegmentedAddress jmpToNopEra = new(0, 0x600);
        SegmentedAddress jmpToRetEra = new(0, 0x610);
        SegmentedAddress b = new(0, 0x620);
        SegmentedAddress bNext = new(0, 0x621);
        WriteJmpShort(jmpToNopEra, 0x1E); // 0x602 + 0x1E = 0x620
        WriteJmpShort(jmpToRetEra, 0x0E); // 0x612 + 0x0E = 0x620
        WriteNop(b);
        WriteRet(bNext);

        // Observe the first jump: explores B, minting a speculative NOP variant at B.
        feeder.GetInstructionFromMemory(jmpToNopEra);
        CfgInstruction nopVariant = feeder.NodeIndex.GetAtAddress(b).Single();
        nopVariant.IsSpeculative.Should().BeTrue();

        // Self-modify B into a RET, then observe the second jump: explores B again and mints a second
        // speculative variant (a RET) because its final signature differs from the NOP variant.
        WriteRet(b);
        feeder.GetInstructionFromMemory(jmpToRetEra);
        feeder.NodeIndex.GetAtAddress(b).Should().HaveCount(2,
            "two self-modified speculative variants now share address B");
        CfgInstruction liveRet = Parser.ParseInstructionAt(b);
        CfgInstruction retVariant = feeder.NodeIndex.GetAtAddressMatchingFinalSignature(b, liveRet.SignatureFinal)
            ?? throw new InvalidOperationException("expected a speculative RET variant at B");
        retVariant.IsSpeculative.Should().BeTrue();
        retVariant.Should().NotBeSameAs(nopVariant);

        // Act: execute at B. Live memory (RET) matches the RET variant, not the first-indexed NOP variant.
        CfgInstruction result = feeder.GetInstructionFromMemory(b);

        // Assert: the matching sibling is promoted in place; the address is not poisoned.
        result.Should().BeSameAs(retVariant,
            "the speculative variant matching live memory must be promoted, not an arbitrary sibling");
        result.IsSpeculative.Should().BeFalse("the matching variant is promoted to observed");
        feeder.NodeIndex.PoisonSet.Should().NotContain(b,
            "a matching sibling variant must not cause the address to be poisoned");
    }
}
