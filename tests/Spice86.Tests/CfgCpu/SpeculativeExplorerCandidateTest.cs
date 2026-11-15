namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.LoadableFile;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Collections.Generic;
using System.Linq;

using Xunit;

/// <summary>
/// Tests for the candidate-based discovery entry points of <see cref="SpeculativeExplorer"/>:
/// source tracking, trusted-bytes gating, pending candidates and retry, the no-poison rule for
/// non-static sources, refused values and the per-site cap.
/// </summary>
public sealed class SpeculativeExplorerCandidateTest : SpeculativeTestBase {
    private const uint ImageStart = 0x2000;
    private const uint ImageSize = 0x200;

    private readonly SpeculativeExplorer _explorer;

    public SpeculativeExplorerCandidateTest() {
        _explorer = CreateExplorer();
    }

    private void RegisterImage() =>
        ImageRegistry.Register(new LoadedImage(LoadedImageKind.DosProgram, "TEST.COM",
            (ushort)(ImageStart >> 4), ImageStart, ImageSize, null));

    private CfgInstruction CreateTrigger() => CreateObservedNode(new SegmentedAddress(0x200, 0x10));

    [Fact]
    public void NonStaticInvalidDecodeDoesNotPoisonTheAddress() {
        // Arrange: invalid opcode inside a trusted image area.
        SegmentedAddress invalid = new(0x200, 0x40);
        uint phys = MemoryUtils.ToPhysicalAddress(invalid.Segment, invalid.Offset);
        Memory.UInt8[phys] = 0x0F;
        Memory.UInt8[phys + 1] = 0xFF;
        RegisterImage();
        CfgInstruction trigger = CreateTrigger();

        // Act
        _explorer.ExploreCandidates(trigger, [
            new DiscoveryCandidate(invalid, null, InstructionSuccessorType.Normal, ExploreSource.TableEntry)
        ]);

        // Assert
        NodeIndex.PoisonSet.Should().NotContain(invalid,
            "a non-static source that decodes as invalid is dropped, never poisoned");
        NodeIndex.HasAddress(invalid).Should().BeFalse();
    }

    [Fact]
    public void StaticInvalidDecodeStillPoisonsTheAddress() {
        // Arrange
        SegmentedAddress invalid = new(0x200, 0x40);
        uint phys = MemoryUtils.ToPhysicalAddress(invalid.Segment, invalid.Offset);
        Memory.UInt8[phys] = 0x0F;
        Memory.UInt8[phys + 1] = 0xFF;
        RegisterImage();
        CfgInstruction trigger = CreateTrigger();

        // Act
        _explorer.ExploreCandidates(trigger, [
            new DiscoveryCandidate(invalid, null, InstructionSuccessorType.Normal, ExploreSource.Static)
        ]);

        // Assert
        NodeIndex.PoisonSet.Should().Contain(invalid,
            "only the existing static-edge exploration poisons invalid decodes");
    }

    [Fact]
    public void GuessedCandidateFallThroughInvalidDecodeDoesNotPoison() {
        // Arrange: a TableEntry candidate lands on a valid NOP whose fall-through is invalid.
        SegmentedAddress entry = new(0x200, 0x40);
        SegmentedAddress fallThrough = new(0x200, 0x41);
        uint phys = MemoryUtils.ToPhysicalAddress(entry.Segment, entry.Offset);
        Memory.UInt8[phys] = 0x90;
        Memory.UInt8[phys + 1] = 0x0F;
        Memory.UInt8[phys + 2] = 0xFF;
        RegisterImage();
        CfgInstruction trigger = CreateTrigger();

        // Act
        _explorer.ExploreCandidates(trigger, [
            new DiscoveryCandidate(entry, null, InstructionSuccessorType.Normal, ExploreSource.TableEntry)
        ]);

        // Assert: the item decoded from the guess enqueues its successors; the invalid
        // fall-through condemns the guess, so the root is swept - and never poisoned.
        NodeIndex.HasAddress(entry).Should().BeFalse(
            "a guess whose own code reaches trusted invalid bytes is swept");
        NodeIndex.PoisonSet.Should().NotContain(fallThrough,
            "an invalid decode descended from a guessed root is dropped, never poisoned");
    }

    [Fact]
    public void StaticExplorationFallThroughInvalidDecodeStillPoisons() {
        // Arrange: an observed NOP's static successors walk a valid NOP into invalid bytes.
        CfgInstruction observed = CreateObservedNode(new SegmentedAddress(0x200, 0x10));
        uint phys = MemoryUtils.ToPhysicalAddress(0x200, 0x11);
        Memory.UInt8[phys] = 0x90;
        Memory.UInt8[phys + 1] = 0x0F;
        Memory.UInt8[phys + 2] = 0xFF;
        RegisterImage();
        SegmentedAddress fallThrough = new(0x200, 0x12);

        // Act
        _explorer.ExploreFrom(observed);

        // Assert
        NodeIndex.PoisonSet.Should().Contain(fallThrough,
            "static exploration from an observed node keeps poisoning invalid decodes");
    }

    [Fact]
    public void DataSourcedCandidateOverChangedBytesIsPendingUntilBytesAreRestored() {
        // Arrange: NOP;RET inside the image, then the first byte is overwritten.
        SegmentedAddress target = new(0x200, 0x50);
        uint phys = MemoryUtils.ToPhysicalAddress(target.Segment, target.Offset);
        Memory.UInt8[phys] = 0x90;
        Memory.UInt8[phys + 1] = 0xC3;
        RegisterImage();
        Memory.UInt8[phys] = 0x40;
        CfgInstruction trigger = CreateTrigger();
        DiscoveryCandidate candidate = new(target, null, InstructionSuccessorType.Normal, ExploreSource.TableEntry);

        // Act: candidate is kept, not decoded, while the bytes no longer hold their loaded values.
        _explorer.ExploreCandidates(trigger, [candidate]);

        // Assert
        NodeIndex.HasAddress(target).Should().BeFalse("changed image bytes are not trusted");

        // Act: the program writes the loaded value back and the trigger retries.
        Memory.UInt8[phys] = 0x90;
        _explorer.RetryPending(trigger);

        // Assert
        CfgInstruction? node = NodeIndex.GetAtAddress(target).FirstOrDefault();
        node.Should().NotBeNull("restored bytes hold their loaded value again");
        node!.IsSpeculative.Should().BeTrue();
        node.DiscoverySource.Should().Be(ExploreSource.TableEntry,
            "the candidate records its source on the node it creates");
    }

    [Fact]
    public void DataSourcedCandidateOutsideEveryImageStaysPending() {
        // Arrange: valid code at an address no image covers.
        SegmentedAddress target = new(0x500, 0x50);
        WriteNop(target);
        WriteRet(new SegmentedAddress(0x500, 0x51));
        CfgInstruction trigger = CreateTrigger();
        DiscoveryCandidate candidate = new(target, null, InstructionSuccessorType.Normal, ExploreSource.PointerValue);

        // Act
        _explorer.ExploreCandidates(trigger, [candidate]);
        _explorer.RetryPending(trigger);

        // Assert
        NodeIndex.HasAddress(target).Should().BeFalse(
            "memory outside every loaded image is never trusted");
    }

    [Fact]
    public void NonDataSourceSkipsTheTrustedBytesCheck() {
        // Arrange: valid code outside every image, reached through a non-data source.
        SegmentedAddress target = new(0x500, 0x60);
        WriteNop(target);
        WriteRet(new SegmentedAddress(0x500, 0x61));
        CfgInstruction trigger = CreateTrigger();

        // Act
        _explorer.ExploreCandidates(trigger, [
            new DiscoveryCandidate(target, null, InstructionSuccessorType.Normal, ExploreSource.PushedReturn)
        ]);

        // Assert
        NodeIndex.HasAddress(target).Should().BeTrue(
            "PushedReturn is not a data source and needs no trusted bytes");
    }

    [Fact]
    public void RetryPendingWithNothingPendingDoesNothing() {
        // Arrange
        CfgInstruction trigger = CreateTrigger();

        // Act
        _explorer.RetryPending(trigger);

        // Assert
        NodeIndex.GetAtAddress(new SegmentedAddress(0x200, 0x50)).Should().BeEmpty();
    }

    [Fact]
    public void RefusedValuesAreDroppedAndNeverPending() {
        // Arrange
        CfgInstruction trigger = CreateTrigger();
        List<DiscoveryCandidate> candidates = [
            new(new SegmentedAddress(0x200, 0x0000), null, InstructionSuccessorType.Normal, ExploreSource.TableEntry),
            new(new SegmentedAddress(0x200, 0xFFFF), null, InstructionSuccessorType.Normal, ExploreSource.TableEntry),
            new(new SegmentedAddress(0x0000, 0x0040), null, InstructionSuccessorType.Normal, ExploreSource.TableEntry),
            new(new SegmentedAddress(0xFFFF, 0x0040), null, InstructionSuccessorType.Normal, ExploreSource.TableEntry)
        ];

        // Act
        _explorer.ExploreCandidates(trigger, candidates);
        _explorer.RetryPending(trigger);

        // Assert
        foreach (DiscoveryCandidate candidate in candidates) {
            NodeIndex.HasAddress(candidate.Target).Should().BeFalse(
                "offset or segment 0x0000/0xFFFF is refused for data-sourced candidates");
        }
    }

    [Fact]
    public void SweptAnchorSiteStopsProposingTargets() {
        // Arrange: NOP anchor at 0x200:0x10 falls through to a speculative `jmp bx` at 0x11.
        // The anchor does not write BX, so the site registers a dependent on it.
        SegmentedAddress anchorAddress = new(0x200, 0x10);
        SegmentedAddress siteAddress = new(0x200, 0x11);
        SegmentedAddress firstTarget = new(0x200, 0x50);
        SegmentedAddress secondTarget = new(0x200, 0x60);
        WriteNop(anchorAddress);
        uint sitePhys = MemoryUtils.ToPhysicalAddress(siteAddress.Segment, siteAddress.Offset);
        Memory.UInt8[sitePhys] = 0xFF;
        Memory.UInt8[sitePhys + 1] = 0xE3;
        WriteNop(firstTarget);
        WriteRet(new SegmentedAddress(0x200, 0x51));
        WriteNop(secondTarget);
        WriteRet(new SegmentedAddress(0x200, 0x61));
        RegisterImage();
        CfgInstruction anchor = Parser.ParseInstructionAt(anchorAddress);
        NodeIndex.Insert(anchor);
        SetAllGeneralRegisters(0x50);
        _explorer.ExploreFrom(anchor);
        _explorer.EvaluateAnchorDependents(anchor);
        NodeIndex.HasAddress(firstTarget).Should().BeTrue("the dependent proposed BX as its target");

        // Act: the sweep discards the site; the anchor fires again with a new register value.
        CfgInstruction site = NodeIndex.GetAtAddress(siteAddress).First();
        new SpeculativeReachabilityPruner(ReplacerRegistry).Sweep(site);
        SetAllGeneralRegisters(0x60);
        _explorer.EvaluateAnchorDependents(anchor);

        // Assert
        NodeIndex.HasAddress(secondTarget).Should().BeFalse(
            "a dependent whose site a sweep removed must be dropped, so the anchor submits nothing");
    }

    [Fact]
    public void SweptPendingPredecessorIsNotRetried() {
        // Arrange: trusted bytes inside the image at 0x200:0x50, then the first byte is
        // overwritten so the data-sourced candidate stays pending on its trigger.
        SegmentedAddress target = new(0x200, 0x50);
        uint phys = MemoryUtils.ToPhysicalAddress(target.Segment, target.Offset);
        Memory.UInt8[phys] = 0x90;
        Memory.UInt8[phys + 1] = 0xC3;
        RegisterImage();
        Memory.UInt8[phys] = 0x40;
        CfgInstruction trigger = CreateTrigger();
        // The predecessor stays IsLive after removal: only the index forgets it, so the
        // dead-predecessor guard of RetryPending does not cover a swept observed node.
        CfgInstruction predecessor = CreateObservedNode(new SegmentedAddress(0x200, 0x20));
        _explorer.ExploreCandidates(trigger, [
            new DiscoveryCandidate(target, predecessor, InstructionSuccessorType.Normal, ExploreSource.TableEntry)
        ]);

        // Act: the sweep removes the predecessor, then the bytes hold their loaded value again.
        new SpeculativeReachabilityPruner(ReplacerRegistry).Sweep(predecessor);
        Memory.UInt8[phys] = 0x90;
        _explorer.RetryPending(trigger);

        // Assert
        NodeIndex.HasAddress(target).Should().BeFalse(
            "a candidate whose predecessor a sweep removed must be dropped, so retrying submits nothing");
    }

    [Fact]
    public void SweptCalleeEntryDropsItsReturnProof() {
        // Arrange: call at 0x200:0x10 calls a callee at 0x80 that is a lone RET; the explorer
        // proves the callee returns and wires the continuation at 0x13.
        WriteCallNear(new SegmentedAddress(0x200, 0x10), new SegmentedAddress(0x200, 0x80));
        WriteNop(new SegmentedAddress(0x200, 0x13));
        WriteRet(new SegmentedAddress(0x200, 0x14));
        WriteRet(new SegmentedAddress(0x200, 0x80));
        WriteCallNear(new SegmentedAddress(0x200, 0x30), new SegmentedAddress(0x200, 0x80));
        WriteNop(new SegmentedAddress(0x200, 0x33));
        WriteRet(new SegmentedAddress(0x200, 0x34));
        RegisterImage();
        CfgInstruction firstCall = Parser.ParseInstructionAt(new SegmentedAddress(0x200, 0x10));
        NodeIndex.Insert(firstCall);
        _explorer.ExploreFrom(firstCall);
        SegmentedAddress firstContinuation = new(0x200, 0x13);
        NodeIndex.HasAddress(firstContinuation).Should().BeTrue(
            "the callee's speculative subgraph proved it returns");

        // Act: the sweep discards the callee entry; a second call site asks for the same callee.
        CfgInstruction calleeEntry = NodeIndex.GetAtAddress(new SegmentedAddress(0x200, 0x80)).First();
        new SpeculativeReachabilityPruner(ReplacerRegistry).Sweep(calleeEntry);
        CfgInstruction secondCall = Parser.ParseInstructionAt(new SegmentedAddress(0x200, 0x30));
        NodeIndex.Insert(secondCall);
        _explorer.ExploreCallContinuationForCallee(secondCall, new SegmentedAddress(0x200, 0x80));

        // Assert
        NodeIndex.HasAddress(new SegmentedAddress(0x200, 0x33)).Should().BeFalse(
            "a proof whose callee entry a sweep removed must not drain new call sites");
    }

    [Fact]
    public void GuessWhoseOwnCodeReachesTrustedInvalidBytesIsSwept() {
        // Arrange: a guess root at 0x50: nop then jnz to bytes that decode invalid and
        // still hold their loaded values.
        SegmentedAddress root = new(0x200, 0x50);
        WriteNop(root);
        WriteInvalidBytes(new SegmentedAddress(0x200, 0x51 + 2 + 8));
        uint sitePhys = MemoryUtils.ToPhysicalAddress(0x200, 0x51);
        Memory.UInt8[sitePhys] = 0x75;
        Memory.Int8[sitePhys + 1] = 8;
        WriteRet(new SegmentedAddress(0x200, 0x53));
        RegisterImage();
        CfgInstruction trigger = CreateTrigger();

        // Act
        _explorer.ExploreCandidates(trigger, [
            new DiscoveryCandidate(root, null, InstructionSuccessorType.Normal, ExploreSource.TableEntry)
        ]);

        // Assert
        NodeIndex.HasAddress(root).Should().BeFalse(
            "a guess whose own code reaches trusted invalid bytes is condemned and swept");
    }

    [Fact]
    public void InvalidCallTargetBelowAGuessDoesNotCondemnIt() {
        // Arrange: a guess root at 0x50 is a call whose callee holds invalid bytes: a
        // call target crosses into code the guess does not own, so it is not evidence.
        SegmentedAddress root = new(0x200, 0x50);
        WriteCallNear(root, new SegmentedAddress(0x200, 0x70));
        WriteInvalidBytes(new SegmentedAddress(0x200, 0x70));
        WriteRet(new SegmentedAddress(0x200, 0x53));
        RegisterImage();
        CfgInstruction trigger = CreateTrigger();

        // Act
        _explorer.ExploreCandidates(trigger, [
            new DiscoveryCandidate(root, null, InstructionSuccessorType.Normal, ExploreSource.TableEntry)
        ]);

        // Assert
        NodeIndex.HasAddress(root).Should().BeTrue(
            "an invalid call target below a guess condemns nothing");
    }

    [Fact]
    public void InvalidCallContinuationBelowAGuessDoesNotCondemnIt() {
        // Arrange: a guess root at 0x50 is a call to a callee that returns; the
        // continuation after the call holds invalid bytes. Call continuations are not
        // evidence either.
        SegmentedAddress root = new(0x200, 0x50);
        WriteCallNear(root, new SegmentedAddress(0x200, 0x70));
        WriteInvalidBytes(new SegmentedAddress(0x200, 0x53));
        WriteRet(new SegmentedAddress(0x200, 0x70));
        RegisterImage();
        CfgInstruction trigger = CreateTrigger();

        // Act
        _explorer.ExploreCandidates(trigger, [
            new DiscoveryCandidate(root, null, InstructionSuccessorType.Normal, ExploreSource.TableEntry)
        ]);

        // Assert
        NodeIndex.HasAddress(root).Should().BeTrue(
            "an invalid call continuation below a guess condemns nothing");
    }

    [Fact]
    public void RejectedGuessIsNotDecodedAgainUntilItsBytesChange() {
        // Arrange: the same invalid-reaching guess, submitted through PushedReturn so
        // changed bytes are not gated on their loaded values.
        SegmentedAddress root = new(0x200, 0x50);
        SegmentedAddress invalid = new(0x200, 0x60);
        WriteNop(root);
        WriteInvalidBytes(invalid);
        uint sitePhys = MemoryUtils.ToPhysicalAddress(0x200, 0x51);
        Memory.UInt8[sitePhys] = 0x75;
        Memory.Int8[sitePhys + 1] = 0x0D;
        WriteRet(new SegmentedAddress(0x200, 0x53));
        RegisterImage();
        CfgInstruction trigger = CreateTrigger();
        DiscoveryCandidate candidate = new(root, null, InstructionSuccessorType.Normal, ExploreSource.PushedReturn);
        _explorer.ExploreCandidates(trigger, [candidate]);
        NodeIndex.HasAddress(root).Should().BeFalse("the first submission was condemned and swept");

        // Act: the same bytes are submitted again while the junk path already decodes validly.
        Memory.UInt8[MemoryUtils.ToPhysicalAddress(invalid.Segment, invalid.Offset)] = 0xC3;
        _explorer.ExploreCandidates(trigger, [candidate]);

        // Assert: a re-submitted guess with unchanged root bytes is skipped entirely.
        NodeIndex.HasAddress(root).Should().BeFalse(
            "a rejected guess is not decoded again while its bytes are unchanged");

        // Act: the root's bytes changed, so its signature differs and it decodes again.
        WriteRet(root);
        _explorer.ExploreCandidates(trigger, [candidate]);

        // Assert
        NodeIndex.HasAddress(root).Should().BeTrue(
            "changed bytes give another signature and decode normally");
    }

    private void WriteInvalidBytes(SegmentedAddress address) {
        uint phys = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        Memory.UInt8[phys] = 0x0F;
        Memory.UInt8[phys + 1] = 0xFF;
    }

    private void SetAllGeneralRegisters(ushort value) {
        for (int i = 0; i < 8; i++) {
            State.GeneralRegisters.UInt16[(uint)i] = value;
        }
    }

    private void WriteCallNear(SegmentedAddress address, SegmentedAddress target) {
        uint phys = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        short relative = (short)(target.Offset - (address.Offset + 3));
        Memory.UInt8[phys] = 0xE8;
        Memory.UInt8[phys + 1] = (byte)relative;
        Memory.UInt8[phys + 2] = (byte)(relative >> 8);
    }

    [Fact]
    public void SiteAcceptsAtMost32DistinctNonStaticTargets() {
        // Arrange: 33 distinct trusted targets reached from the same site, each a RET.
        CfgInstruction site = CreateObservedNode(new SegmentedAddress(0x200, 0x20));
        List<DiscoveryCandidate> candidates = [];
        for (int i = 0; i < 33; i++) {
            SegmentedAddress target = new(0x200, (ushort)(0x80 + i));
            WriteRet(target);
            candidates.Add(new DiscoveryCandidate(target, site, InstructionSuccessorType.Normal, ExploreSource.TableEntry));
        }
        RegisterImage();

        // Act
        _explorer.ExploreCandidates(CreateTrigger(), candidates);

        // Assert: 32 accepted, the 33rd dropped.
        int decoded = candidates.Count(c => NodeIndex.HasAddress(c.Target));
        decoded.Should().Be(32, "the per-site cap drops targets past 32 distinct ones");
        NodeIndex.HasAddress(candidates[32].Target).Should().BeFalse();
    }
}
