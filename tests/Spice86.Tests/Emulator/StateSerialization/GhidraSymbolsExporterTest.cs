namespace Spice86.Tests.Emulator.StateSerialization;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// The data symbols of <see cref="GhidraSymbolsExporter.Write"/>: written as label lines, one per
/// address, never at an address a function or a jump label already holds.
/// </summary>
public sealed class GhidraSymbolsExporterTest {
    private static string WriteLines(ExecutionAddresses executionAddresses,
        FunctionCatalogue functionCatalogue,
        IReadOnlyList<(string Name, SegmentedAddress Address)> dataSymbols) {
        string path = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try {
            new GhidraSymbolsExporter(NullLogger.Instance)
                .Write(executionAddresses, functionCatalogue, dataSymbols, path);
            return File.ReadAllText(path);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void DataSymbolsAtTwoSegmentsGiveTwoLabelLines() {
        // Arrange: the same CS global accessed from two code segments.
        ExecutionAddresses executionAddresses = new();
        FunctionCatalogue functionCatalogue = new();
        IReadOnlyList<(string Name, SegmentedAddress Address)> dataSymbols = [
            ("PlayerScore", new SegmentedAddress(0xD000, 0x0030)),
            ("PlayerScore", new SegmentedAddress(0xF000, 0x0030))
        ];

        // Act
        string text = WriteLines(executionAddresses, functionCatalogue, dataSymbols);

        // Assert
        text.Should().Contain("PlayerScore_D000_0030_D0030 ");
        text.Should().Contain("PlayerScore_F000_0030_F0030 ");
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.TrimEnd('\r').EndsWith(" l")).Should().Be(2);
    }

    [Fact]
    public void LocationClassPropertyGivesOneLabelAtSegmentValueTimesSixteenPlusOffset() {
        // Arrange: a location property of value 0x2000 at offset 0x0030.
        ExecutionAddresses executionAddresses = new();
        FunctionCatalogue functionCatalogue = new();
        IReadOnlyList<(string Name, SegmentedAddress Address)> dataSymbols = [
            ("Word0030", new SegmentedAddress(0x2000, 0x0030))
        ];

        // Act
        string text = WriteLines(executionAddresses, functionCatalogue, dataSymbols);

        // Assert: one l line at linear 0x20030.
        text.Should().Contain("Word0030_2000_0030_20030 ");
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.TrimEnd('\r').EndsWith(" l")).Should().Be(1);
    }

    [Fact]
    public void DataSymbolAtAFunctionOrLabelAddressIsNotWritten() {
        // Arrange: one data symbol shares a function's address, another a jump label's, a third is free.
        ExecutionAddresses executionAddresses = new();
        SegmentedAddress labelAddress = new(0x1234, 0x0010);
        executionAddresses.JumpsFromTo[0x1000] = [labelAddress];
        SegmentedAddress functionAddress = new(0xF000, 0x0100);
        FunctionCatalogue functionCatalogue = new([new FunctionInformation(functionAddress, "Func")]);
        IReadOnlyList<(string Name, SegmentedAddress Address)> dataSymbols = [
            ("AtFunction", functionAddress),
            ("AtLabel", labelAddress),
            ("Free", new SegmentedAddress(0xD000, 0x0040))
        ];

        // Act
        string text = WriteLines(executionAddresses, functionCatalogue, dataSymbols);

        // Assert
        text.Should().NotContain("AtFunction_");
        text.Should().NotContain("AtLabel_");
        text.Should().Contain("Free_D000_0040_D0040 ");
    }

    [Fact]
    public void TwoDataSymbolsAtOneAddressWriteTheFirstOnce() {
        // Arrange: two names registered at the same address, "First" first in input order.
        ExecutionAddresses executionAddresses = new();
        FunctionCatalogue functionCatalogue = new();
        SegmentedAddress address = new(0xD000, 0x0050);
        IReadOnlyList<(string Name, SegmentedAddress Address)> dataSymbols = [
            ("First", address),
            ("Second", address)
        ];

        // Act
        string text = WriteLines(executionAddresses, functionCatalogue, dataSymbols);

        // Assert
        text.Should().Contain("First_D000_0050_D0050 ");
        text.Should().NotContain("Second_");
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.TrimEnd('\r').EndsWith(" l")).Should().Be(1);
    }
}
