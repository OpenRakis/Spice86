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
