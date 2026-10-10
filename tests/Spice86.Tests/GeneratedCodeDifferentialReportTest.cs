namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>Formatting tests of the line that reports the final-state difference of two runs.</summary>
public sealed class GeneratedCodeDifferentialReportTest {
    /// <summary>Equal registers and equal RAM are reported as identical.</summary>
    [Fact]
    public void DescribeReportsIdenticalWhenRegistersAndRamAreEqual() {
        // Arrange
        AsmFinalState emulated = BuildState(0x1234, 0x5678, new byte[16]);
        AsmFinalState generated = BuildState(0x1234, 0x5678, new byte[16]);

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k", emulated, generated);

        // Assert
        line.Should().Be("k | identical");
    }

    /// <summary>Differing registers are listed by name and the RAM difference is summarised as ranges.</summary>
    [Fact]
    public void DescribeListsDifferentRegistersAndRamRanges() {
        // Arrange
        byte[] emulatedRam = new byte[16];
        byte[] generatedRam = new byte[16];
        generatedRam[2] = 1;
        generatedRam[3] = 1;
        generatedRam[7] = 1;
        AsmFinalState emulated = BuildState(0x1234, 0x5678, emulatedRam);
        AsmFinalState generated = BuildState(0x5678, 0x5678, generatedRam);

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k", emulated, generated);

        // Assert
        line.Should().Be(
            "k | registers: AX 0x1234->0x5678 | ram: 3 bytes in 2 ranges: 0x00002-0x00003, 0x00007-0x00007");
    }

    /// <summary>Only the first ten RAM ranges are written, followed by an ellipsis.</summary>
    [Fact]
    public void DescribeWritesOnlyTheFirstTenRanges() {
        // Arrange
        byte[] emulatedRam = new byte[32];
        byte[] generatedRam = new byte[32];
        for (int i = 0; i < generatedRam.Length; i += 2) {
            generatedRam[i] = 1;
        }
        AsmFinalState emulated = BuildState(0x1234, 0x5678, emulatedRam);
        AsmFinalState generated = BuildState(0x1234, 0x5678, generatedRam);

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k", emulated, generated);

        // Assert
        line.Should().EndWith(", ...");
        CountRanges(line).Should().Be(10, "only the first ten ranges must be written");
    }

    private static AsmFinalState BuildState(ushort ax, ushort bx, byte[] ram) {
        (string Name, ushort Value)[] registers = [("AX", ax), ("BX", bx)];
        return new AsmFinalState(registers, ram);
    }

    private static int CountRanges(string line) {
        return line.Split("-0x").Length - 1;
    }
}
