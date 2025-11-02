namespace Spice86.Tests.Emulator.Gdb;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.Gdb;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Unit tests for GdbIo protocol message generation.
/// </summary>
public class GdbIoTests {
    private readonly ILoggerService _loggerService;

    public GdbIoTests() {
        _loggerService = Substitute.For<ILoggerService>();
    }

    [Fact]
    public void GenerateResponse_ShouldFormatWithPlusSignDollarSignAndChecksum() {
        // Arrange
        using GdbIo gdbIo = new(0, _loggerService);
        const string testData = "OK";

        // Act
        string result = gdbIo.GenerateResponse(testData);

        // Assert
        // Format: +$data#checksum
        // Checksum is sum of bytes in data (O=0x4F, K=0x4B = 0x9A)
        result.Should().StartWith("+$");
        result.Should().Contain("OK#");
        result.Should().EndWith("9A");
    }

    [Theory]
    [InlineData("", "00")] // Empty string has checksum 0
    [InlineData("c", "63")] // 'c' = 0x63
    [InlineData("OK", "9A")] // 'O' + 'K' = 0x4F + 0x4B = 0x9A
    [InlineData("qSupported", "37")] // Sum wraps at 8 bits: 0x337 % 256 = 0x37
    public void GenerateResponse_ShouldCalculateCorrectChecksum(string data, string expectedChecksum) {
        // Arrange
        using GdbIo gdbIo = new(0, _loggerService);

        // Act
        string result = gdbIo.GenerateResponse(data);

        // Assert
        result.Should().EndWith($"#{expectedChecksum}");
    }

    [Fact]
    public void GenerateMessageToDisplayResponse_ShouldHexEncodeMessageAndAddNewline() {
        // Arrange
        using GdbIo gdbIo = new(0, _loggerService);
        const string message = "Test";

        // Act
        string result = gdbIo.GenerateMessageToDisplayResponse(message);

        // Assert
        // "Test\n" -> hex encoded
        // T=0x54, e=0x65, s=0x73, t=0x74, \n=0x0A
        result.Should().Contain("546573740A");
    }

    [Fact]
    public void GenerateUnsupportedResponse_ShouldReturnEmptyString() {
        // Arrange
        using GdbIo gdbIo = new(0, _loggerService);

        // Act
        string result = gdbIo.GenerateUnsupportedResponse();

        // Assert
        result.Should().BeEmpty();
    }
}
