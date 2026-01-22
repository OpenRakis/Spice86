namespace Spice86.Tests.Emulator.Memory;

using FluentAssertions;
using Spice86;
using Spice86.Core.Emulator.Memory;
using Xunit;

public class SearchValueTests {
    private const string DefaultBinName = "jump2";

    private Spice86DependencyInjection CreateDependencies() {
        return new Spice86Creator(DefaultBinName, installInterruptVectors: true).Create();
    }

    [Fact]
    public void FindsPatternAtStart() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = CreateDependencies();
        Memory memory = (Memory)spice86DependencyInjection.Machine.Memory;
        byte[] pattern = [0x10, 0x20, 0x30];
        memory.LoadData(0, pattern);

        // Act
        uint? result = memory.SearchValue(0, pattern.Length, pattern);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void ReturnsNullWhenPatternNotPresent() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = CreateDependencies();
        Memory memory = (Memory)spice86DependencyInjection.Machine.Memory;
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        memory.LoadData(0, data);
        byte[] pattern = [0xAA, 0xBB];

        // Act
        uint? result = memory.SearchValue(0, data.Length, pattern);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RespectsSearchLengthLimit() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = CreateDependencies();
        Memory memory = (Memory)spice86DependencyInjection.Machine.Memory;
        byte[] data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x22, 0x33];
        memory.LoadData(0, data);
        byte[] pattern = [0x11, 0x22, 0x33];

        // Act
        uint? resultWithinLimit = memory.SearchValue(0, 5, pattern);
        uint? resultWithFullRange = memory.SearchValue(0, data.Length, pattern);

        // Assert
        resultWithinLimit.Should().BeNull();
        resultWithFullRange.Should().Be(5);
    }

    [Fact]
    public void FindsFirstOccurrenceWhenMultipleExist() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = CreateDependencies();
        Memory memory = (Memory)spice86DependencyInjection.Machine.Memory;
        byte[] data = [0x99, 0x01, 0x02, 0x99, 0x01, 0x02, 0x03];
        memory.LoadData(0, data);
        byte[] pattern = [0x99, 0x01];

        // Act
        uint? result = memory.SearchValue(0, data.Length, pattern);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void FindsPatternAtEndOfMemory() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = CreateDependencies();
        Memory memory = (Memory)spice86DependencyInjection.Machine.Memory;
        byte[] pattern = [0xDE, 0xAD, 0xBE, 0xEF];
        uint tailStart = 0xFFFC;
        memory.LoadData(tailStart, pattern);

        // Act
        uint? result = memory.SearchValue(tailStart, pattern.Length, pattern);

        // Assert
        result.Should().Be(tailStart);
    }

    [Fact]
    public void ReturnsNullForEmptyPattern() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = CreateDependencies();
        Memory memory = (Memory)spice86DependencyInjection.Machine.Memory;
        byte[] data = [0x01, 0x02, 0x03];
        memory.LoadData(0, data);
        byte[] emptyPattern = [];

        // Act
        uint? result = memory.SearchValue(0, data.Length, emptyPattern);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ReturnsNullWhenPatternLongerThanSearchRange() {
        // Arrange
        using Spice86DependencyInjection spice86DependencyInjection = CreateDependencies();
        Memory memory = (Memory)spice86DependencyInjection.Machine.Memory;
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        memory.LoadData(0, data);
        byte[] longPattern = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];

        // Act
        uint? result = memory.SearchValue(0, 3, longPattern);

        // Assert
        result.Should().BeNull();
    }
}
