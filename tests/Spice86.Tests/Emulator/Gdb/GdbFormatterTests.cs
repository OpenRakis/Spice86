namespace Spice86.Tests.Emulator.Gdb;

using FluentAssertions;

using Spice86.Core.Emulator.Gdb;

using Xunit;

/// <summary>
/// Unit tests for the GdbFormatter class to ensure proper formatting of values for GDB protocol.
/// </summary>
public class GdbFormatterTests {
    private readonly GdbFormatter _formatter = new();

    [Theory]
    [InlineData(0x12345678u, "78563412")]
    [InlineData(0x00000000u, "00000000")]
    [InlineData(0xFFFFFFFFu, "FFFFFFFF")]
    [InlineData(0x00000001u, "01000000")]
    [InlineData(0xABCDEF01u, "01EFCDAB")]
    public void FormatValueAsHex32_ShouldReturnSwappedLittleEndianHex(uint value, string expected) {
        // Act
        string result = _formatter.FormatValueAsHex32(value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0x00, "00")]
    [InlineData(0xFF, "FF")]
    [InlineData(0x12, "12")]
    [InlineData(0xAB, "AB")]
    public void FormatValueAsHex8_ShouldReturnHexString(byte value, string expected) {
        // Act
        string result = _formatter.FormatValueAsHex8(value);

        // Assert
        result.Should().Be(expected);
    }
}
