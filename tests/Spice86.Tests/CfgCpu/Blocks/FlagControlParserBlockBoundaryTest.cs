namespace Spice86.Tests.CfgCpu.Blocks;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Verifies that <see cref="Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers.FlagControlParser"/>
/// flags STI and CLI as block boundaries at parse time.
/// </summary>
public class FlagControlParserBlockBoundaryTest {
    private readonly TestInstructionHelper _helper = new();

    /// <summary>
    /// STI (opcode 0xFB) must end its CfgBlock so that external interrupt delivery happens
    /// at the boundary just after interrupts are enabled (after the one-instruction shadow).
    /// </summary>
    [Fact]
    public void Sti_SetsExplicitTerminatorFlag() {
        // Arrange
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => w.WriteUInt8(0xFB));

        // Assert
        instruction.IsBlockTerminator.Should().BeTrue(
            "the STI parser must mark the parsed instruction as a block terminator");
    }

    /// <summary>
    /// CLI (opcode 0xFA) must start a new CfgBlock so that external interrupt delivery happens
    /// at the boundary just before interrupts are disabled.
    /// </summary>
    [Fact]
    public void Cli_SetsExplicitStarterFlag() {
        // Arrange
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => w.WriteUInt8(0xFA));

        // Assert
        instruction.IsBlockStarter.Should().BeTrue(
            "the CLI parser must mark the parsed instruction as a block starter");
    }

    /// <summary>
    /// POPF (opcode 0x9D) must be simultaneously a block terminator and a block starter.
    /// It is a terminator because it restores flags (including the interrupt flag) from the stack,
    /// and a starter so that any preceding instructions are sealed into their own block before
    /// POPF takes effect.
    /// </summary>
    [Fact]
    public void Popf_SetsBothStarterAndTerminatorFlags() {
        // Arrange
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => w.WriteUInt8(0x9D));

        // Assert
        instruction.IsBlockTerminator.Should().BeTrue(
            "the POPF parser must mark the instruction as a block terminator");
        instruction.IsBlockStarter.Should().BeTrue(
            "the POPF parser must mark the instruction as a block starter");
    }
}
