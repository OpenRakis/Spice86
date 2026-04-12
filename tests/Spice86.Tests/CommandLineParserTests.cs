namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;

using System.IO;

using Xunit;

public class CommandLineParserTests {

    [Fact]
    public void TestParseHexDecBin() {
        // nothing to convert
        Assert.Throws<FormatException>(() => CommandLineParser.ParseHexDecBinInt64(""));

        // the converter is based on Convert.Int64 routine - so min/max ranges etc. don't need check
        // no negatives with binary and hex

        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("2748"));
        Assert.Equal(-2748, CommandLineParser.ParseHexDecBinInt64("-2748"));

        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0XABC"));
        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0xABC"));
        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0xabc"));
        Assert.Throws<FormatException>(() => CommandLineParser.ParseHexDecBinInt64("-0xABC"));

        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0b101010111100"));
        Assert.Equal(2748, CommandLineParser.ParseHexDecBinInt64("0B101010111100"));
        Assert.Throws<FormatException>(() => CommandLineParser.ParseHexDecBinInt64("-0b101010111100"));

        Assert.Equal(65535, CommandLineParser.ParseHexDecBinUInt16("65535"));
        Assert.Equal(65535, CommandLineParser.ParseHexDecBinUInt16("0xFFFF"));
        Assert.Equal(65535, CommandLineParser.ParseHexDecBinUInt16("0b1111111111111111"));

        Assert.Throws<FormatException>(() => CommandLineParser.ParseHexDecBinUInt16("165535"));
    }

    [Fact]
    public void ParseCommandLine_ShouldPreserveExeArgsAndNullableBooleans() {
        // Arrange
        string executablePath = CreateTemporaryExecutablePath();
        try {
            string[] args =
            [
                "-e", executablePath,
                "-a", "   \"ab\"  cd",
                "-u", "false",
                "-h", "Minimal",
                "-p", "0x180"
            ];

            // Act
            Configuration? configuration = Parse(args);

            // Assert
            configuration.Should().NotBeNull();
            Configuration nonNullConfiguration = configuration ?? throw new InvalidOperationException("Configuration should not be null.");
            nonNullConfiguration.ExeArgs.Should().Be("   \"ab\"  cd");
            nonNullConfiguration.UseCodeOverride.Should().BeFalse();
            nonNullConfiguration.UseCodeOverrideOption.Should().BeFalse();
            nonNullConfiguration.HeadlessMode.Should().Be(HeadlessType.Minimal);
            nonNullConfiguration.ProgramEntryPointSegment.Should().Be(0x180);
        } finally {
            File.Delete(executablePath);
        }
    }

    [Fact]
    public void ParseCommandLine_ShouldApplyPostProcessingRules() {
        // Arrange
        string executablePath = CreateTemporaryExecutablePath();
        try {
            string[] args =
            [
                "-e", executablePath,
                "--Cycles", "123",
                "--InstructionTimeScale", "456",
                "--CpuHeavyLogDumpFile", "cpu-heavy.log"
            ];

            // Act
            Configuration? configuration = Parse(args);

            // Assert
            configuration.Should().NotBeNull();
            Configuration nonNullConfiguration = configuration ?? throw new InvalidOperationException("Configuration should not be null.");
            nonNullConfiguration.Cycles.Should().Be(123);
            nonNullConfiguration.InstructionTimeScale.Should().BeNull();
            nonNullConfiguration.CpuHeavyLog.Should().BeTrue();
            nonNullConfiguration.CpuHeavyLogDumpFile.Should().Be("cpu-heavy.log");
        } finally {
            File.Delete(executablePath);
        }
    }

    [Fact]
    public void ParseCommandLine_ShouldReturnNullWhenRequiredOptionMissing() {
        // Arrange
        string[] args = ["--Debug"];

        // Act
        Configuration? configuration = Parse(args);

        // Assert
        configuration.Should().BeNull();
    }

    private static Configuration? Parse(string[] args) {
        CommandLineParser parser = new();
        return parser.ParseCommandLine(args);
    }

    private static string CreateTemporaryExecutablePath() {
        string filePath = Path.GetTempFileName();
        return filePath;
    }
}