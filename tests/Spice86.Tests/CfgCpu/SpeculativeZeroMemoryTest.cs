namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;

using System.Linq;

using Xunit;

/// <summary>
/// The explorer must stop a speculative run at a chain of zero instructions: a branch into a
/// zero-filled buffer decodes nothing, a single <c>00 00</c> followed by code is still decoded, an
/// existing node at the address is still linked, and a known-safe seed on a zero chain decodes
/// nothing. The check reads the memory like an analysis, not like the program: no read breakpoint
/// fires, and the next instruction's offset wraps to 16 bits like a fetch would.
/// </summary>
public sealed class SpeculativeZeroMemoryTest : SpeculativeTestBase {
    private readonly SpeculativeExplorer _explorer;

    public SpeculativeZeroMemoryTest() {
        _explorer = new SpeculativeExplorer(Memory, Parser, NodeIndex, NodeLinker);
    }

    private CfgInstruction ObservedJnzWithCodeFallthrough() {
        // JNZ at 0:0x100, rel8 +4: fall-through 0x102 (NOP), 0x103 (RET), branch target 0x106.
        WriteNop(new SegmentedAddress(0, 0x102));
        WriteRet(new SegmentedAddress(0, 0x103));
        CfgInstruction jnz = WriteConditionalJnzAndParse(new SegmentedAddress(0, 0x100), 4);
        NodeIndex.Insert(jnz);
        return jnz;
    }

    [Fact]
    public void ChainOfZeroInstructionsIsNotDecoded() {
        // Arrange
        CfgInstruction jnz = ObservedJnzWithCodeFallthrough();
        // 0x106..0x109 stay zero: the target is a chain of zero instructions.

        // Act
        _explorer.ExploreFrom(jnz);

        // Assert
        SegmentedAddress zeroTarget = new(0, 0x106);
        NodeIndex.HasAddress(zeroTarget).Should().BeFalse("a zero-instruction chain must not be decoded");
        jnz.Successors.Should().NotContain(node => node.Address.Equals(zeroTarget),
            "the JNZ must have no successor at the zero-chain target");
        NodeIndex.PoisonSet.Should().NotContain(zeroTarget,
            "a skipped zero chain is not remembered: memory later filled with code must decode normally");

        CfgInstruction fallThrough = NodeIndex.GetAtAddress(new SegmentedAddress(0, 0x102)).First();
        fallThrough.IsSpeculative.Should().BeTrue("the non-zero fall-through is still decoded");
    }

    [Fact]
    public void SingleZeroInstructionFollowedByCodeIsDecoded() {
        // Arrange
        CfgInstruction jnz = ObservedJnzWithCodeFallthrough();
        // 0x106..0x107 zero, code at 0x108 (NOP) and 0x109 (RET): a single zero instruction.
        WriteNop(new SegmentedAddress(0, 0x108));
        WriteRet(new SegmentedAddress(0, 0x109));

        // Act
        _explorer.ExploreFrom(jnz);

        // Assert
        SegmentedAddress zeroAddress = new(0, 0x106);
        NodeIndex.HasAddress(zeroAddress).Should().BeTrue(
            "a lone zero instruction followed by code is still decoded");
        CfgInstruction zeroNode = NodeIndex.GetAtAddress(zeroAddress).First();
        zeroNode.Length.Should().Be(2);
        jnz.Successors.Should().Contain(zeroNode);
    }

    [Fact]
    public void ExistingNodeOnAZeroChainIsStillLinked() {
        // Arrange
        CfgInstruction jnz = ObservedJnzWithCodeFallthrough();
        // 0x106..0x109 stay zero, but a node already exists at the target (observed node).
        SegmentedAddress zeroTarget = new(0, 0x106);
        CfgInstruction existing = Parser.ParseInstructionAt(zeroTarget);
        NodeIndex.Insert(existing);

        // Act
        _explorer.ExploreFrom(jnz);

        // Assert
        jnz.Successors.Should().Contain(existing,
            "the convergence check runs before the zero-chain stop, so an existing node is linked");
    }

    [Fact]
    public void KnownSafeSeedOnAZeroChainDecodesNothing() {
        // Arrange
        SegmentedAddress entry = new(0, 0x200);
        // 0x200..0x203 stay zero.

        // Act
        _explorer.SeedKnownSafe(entry);

        // Assert
        NodeIndex.HasAddress(entry).Should().BeFalse(
            "a known-safe seed on a zero chain decodes nothing");
        NodeIndex.PoisonSet.Should().NotContain(entry);
    }

    [Fact]
    public void ZeroChainCheckFiresNoReadBreakpointOnTheZeroBytes() {
        // Arrange
        CfgInstruction jnz = ObservedJnzWithCodeFallthrough();
        // 0x106..0x109 stay zero; the chain check reads 0x108 and 0x109.
        int readHits = 0;
        AddressBreakPoint breakPoint = new(BreakPointType.MEMORY_READ, 0x108,
            (_, _) => readHits++, isRemovedOnTrigger: false);
        AddressBreakPoint breakPoint2 = new(BreakPointType.MEMORY_READ, 0x109,
            (_, _) => readHits++, isRemovedOnTrigger: false);
        MemoryBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.READ, true);
        MemoryBreakpoints.ToggleBreakPoint(breakPoint2, AddressOperation.READ, true);

        // Act
        _explorer.ExploreFrom(jnz);

        // Assert
        readHits.Should().Be(0,
            "the zero-chain check is analysis: no read breakpoint fires although the program never read those bytes");
        NodeIndex.HasAddress(new SegmentedAddress(0, 0x106)).Should().BeFalse();
    }

    [Fact]
    public void ZeroChainNextOffsetWrapsTo16BitsLikeAFetch() {
        // Arrange: a 00 00 at 0:0xFFFE, so the next instruction starts at 0:0x10000; a fetch wraps
        // the offset to 16 bits, so the two bytes read are 0:0x0000 and 0:0x0001.
        CfgInstruction jnz = WriteConditionalJnzAndParse(new SegmentedAddress(0, 0xFFFC), 0); // target 0xFFFE
        int readHits = 0;
        AddressBreakPoint breakPoint = new(BreakPointType.MEMORY_READ, 0x0000,
            (_, _) => readHits++, isRemovedOnTrigger: false);
        AddressBreakPoint breakPoint2 = new(BreakPointType.MEMORY_READ, 0x0001,
            (_, _) => readHits++, isRemovedOnTrigger: false);
        MemoryBreakpoints.ToggleBreakPoint(breakPoint, AddressOperation.READ, true);
        MemoryBreakpoints.ToggleBreakPoint(breakPoint2, AddressOperation.READ, true);
        NodeIndex.Insert(jnz);

        // Act
        _explorer.ExploreFrom(jnz);

        // Assert
        NodeIndex.HasAddress(new SegmentedAddress(0, 0xFFFE)).Should().BeFalse(
            "0:0x0000 and 0:0x0001 are zero, so the 00 00 at 0xFFFE is a zero chain");
        readHits.Should().Be(0, "the wrapped reads are analysis reads: no read breakpoint fires");
    }
}
