namespace Spice86.Tests.Emulator.Memory;

using FluentAssertions;
using Spice86;
using Spice86.Core.Emulator.Memory;
using Xunit;

public class SearchValueTests {
    private const string DefaultBinName = "jump2";
    private readonly Spice86DependencyInjection _spice86DependencyInjection;
    private readonly Memory _memory;

    public SearchValueTests() {
        _spice86DependencyInjection = new Spice86Creator(DefaultBinName, installInterruptVectors: true).Create();
        _memory = (Memory)_spice86DependencyInjection.Machine.Memory;
    }

    [Fact]
    public void FindsPatternAtStart() {
        // Arrange
        byte[] pattern = [0x10, 0x20, 0x30];
        _memory.LoadData(0, pattern);

        // Act
        uint? result = _memory.SearchValue(0, pattern.Length, pattern);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void ReturnsNullWhenPatternNotPresent() {
        // Arrange
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        _memory.LoadData(0, data);
        byte[] pattern = [0xAA, 0xBB];

        // Act
        uint? result = _memory.SearchValue(0, data.Length, pattern);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RespectsSearchLengthLimit() {
        // Arrange
        byte[] data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x22, 0x33];
        _memory.LoadData(0, data);
        byte[] pattern = [0x11, 0x22, 0x33];

        // Act
        uint? resultWithinLimit = _memory.SearchValue(0, 5, pattern);
        uint? resultWithFullRange = _memory.SearchValue(0, data.Length, pattern);

        // Assert
        resultWithinLimit.Should().BeNull();
        resultWithFullRange.Should().Be(5);
    }

    [Fact]
    public void FindsFirstOccurrenceWhenMultipleExist() {
        // Arrange
        byte[] data = [0x99, 0x01, 0x02, 0x99, 0x01, 0x02, 0x03];
        _memory.LoadData(0, data);
        byte[] pattern = [0x99, 0x01];

        // Act
        uint? result = _memory.SearchValue(0, data.Length, pattern);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void FindsPatternAtEndOfMemory() {
        // Arrange
        byte[] pattern = [0xDE, 0xAD, 0xBE, 0xEF];
        uint tailStart = 0xFFFC;
        _memory.LoadData(tailStart, pattern);

        // Act
        uint? result = _memory.SearchValue(tailStart, pattern.Length, pattern);

        // Assert
        result.Should().Be(tailStart);
    }
}
