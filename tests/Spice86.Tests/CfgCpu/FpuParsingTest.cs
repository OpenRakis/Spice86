namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Tests that FPU opcodes are parsed as single-byte opcodes with ModRM,
/// consistent with the x86 ISA (0xD8-0xDF are FPU escape opcodes, not two-byte prefixes).
/// </summary>
public class FpuParsingTest {
    private readonly TestInstructionHelper _helper = new();

    [Fact]
    public void ParseFnInit_ShouldProduceTwoByteInstruction() {
        // Arrange: FNINIT is encoded as 0xDB 0xE3
        // 0xDB = FPU escape opcode (single byte)
        // 0xE3 = ModRM byte (mode=3, reg=4, r/m=3)
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => {
            w.WriteUInt8(0xDB);
            w.WriteUInt8(0xE3);
        });

        // Assert
        instruction.Length.Should().Be(2, "FNINIT is a 2-byte instruction (opcode + ModRM)");
        string rendered = _helper.RenderDisplayAst(instruction);
        rendered.Should().ContainEquivalentOf("fninit");
    }

    [Fact]
    public void ParseFnInit_OpcodeFieldShouldBeSingleByte() {
        // Arrange: After the fix, 0xDB should be read as a 1-byte opcode,
        // not as part of a 2-byte opcode prefix like 0x0F.
        SegmentedAddress address = new(0x0000, 0x0000);

        // Act
        CfgInstruction instruction = _helper.WriteAndParse(address, w => {
            w.WriteUInt8(0xDB);
            w.WriteUInt8(0xE3);
        });

        // Assert: opcode value should be 0xDB (single byte), not 0xDBE3 (two bytes)
        instruction.OpcodeField.Value.Should().Be(0xDB,
            "0xDB is a single-byte FPU escape opcode, not a two-byte prefix");
        instruction.OpcodeField.Length.Should().Be(1,
            "the opcode field should consume only 1 byte");
    }
}
