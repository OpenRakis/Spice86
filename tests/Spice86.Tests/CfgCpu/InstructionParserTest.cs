namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using Xunit;

/// <summary>
/// The static successors <see cref="Core.Emulator.CPU.CfgCpu.Parser.InstructionParser.ParseInstructionAt"/>
/// registers on a parsed instruction.
/// </summary>
public sealed class InstructionParserTest : SpeculativeTestBase {
    private CfgInstruction WriteBytesAndParse(SegmentedAddress address, params byte[] bytes) {
        uint physAddr = MemoryUtils.ToPhysicalAddress(address.Segment, address.Offset);
        for (int i = 0; i < bytes.Length; i++) {
            Memory.UInt8[physAddr + (uint)i] = bytes[i];
        }
        return Parser.ParseInstructionAt(address);
    }

    [Fact]
    public void StiEndsItsBlockButFallsThroughToTheNextInstructionInMemory() {
        SegmentedAddress address = new(0, 0x100);
        CfgInstruction parsed = WriteBytesAndParse(address, 0xFB);

        parsed.IsBlockTerminator.Should().BeTrue();
        parsed.StaticSuccessorAddresses.Should().Equal(new SegmentedAddress(0, 0x101));
    }

    [Fact]
    public void PopfEndsItsBlockButFallsThroughToTheNextInstructionInMemory() {
        SegmentedAddress address = new(0, 0x100);
        CfgInstruction parsed = WriteBytesAndParse(address, 0x9D);

        parsed.IsBlockTerminator.Should().BeTrue();
        parsed.StaticSuccessorAddresses.Should().Equal(new SegmentedAddress(0, 0x101));
    }

    [Fact]
    public void PopfdEndsItsBlockButFallsThroughToTheNextInstructionInMemory() {
        SegmentedAddress address = new(0, 0x100);
        CfgInstruction parsed = WriteBytesAndParse(address, 0x66, 0x9D);

        parsed.IsBlockTerminator.Should().BeTrue();
        parsed.StaticSuccessorAddresses.Should().Equal(new SegmentedAddress(0, 0x102));
    }

    [Fact]
    public void RetRegistersNoStaticSuccessor() {
        SegmentedAddress address = new(0, 0x100);
        CfgInstruction parsed = WriteBytesAndParse(address, 0xC3);

        parsed.StaticSuccessorAddresses.Should().BeEmpty();
    }
}
