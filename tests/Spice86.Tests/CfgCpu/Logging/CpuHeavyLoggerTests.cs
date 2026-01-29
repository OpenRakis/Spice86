namespace Spice86.Tests.CfgCpu.Logging;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;

using Xunit;

public class CpuHeavyLoggerTests : IDisposable {
    private readonly DumpFolderMetadata _dumpContext;
    private readonly List<string> _filesToCleanup = new();

    public CpuHeavyLoggerTests() {
        string tempExeFile =
            // Create a temporary exe file for DumpFolderMetadata
            Path.GetTempFileName();
        byte[] testData = "test"u8.ToArray();
        File.WriteAllBytes(tempExeFile, testData);
        
        _dumpContext = new DumpFolderMetadata(tempExeFile, Path.GetTempPath());
        _filesToCleanup.Add(tempExeFile);
    }

    public void Dispose() {
        foreach (string file in _filesToCleanup.Where(File.Exists)) {
            File.Delete(file);
        }
    }

    private string GetUniqueLogPath() {
        string path = Path.Join(Path.GetTempPath(), $"test_cpu_log_{Guid.NewGuid()}.log");
        _filesToCleanup.Add(path);
        return path;
    }

    [Fact]
    public void Constructor_WithDefaultPath_CreatesLogFileInDumpDirectory() {
        // Arrange
        string expectedLogPath = Path.Join(_dumpContext.DumpDirectory, "cpu_heavy.log");
        _filesToCleanup.Add(expectedLogPath);

        // Act
        using CpuHeavyLogger logger = new(_dumpContext, null);

        // Assert
        File.Exists(expectedLogPath).Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithCustomPath_CreatesLogFileAtCustomLocation() {
        // Arrange
        string customLogPath = GetUniqueLogPath();

        // Act
        using CpuHeavyLogger logger = new(_dumpContext, customLogPath);

        // Assert
        File.Exists(customLogPath).Should().BeTrue();
    }

    [Fact]
    public void LogInstruction_MultipleInstructions_WritesAllInstructions() {
        // Arrange
        string logPath = GetUniqueLogPath();
        Nop[] nodes = {
            CreateNop(new SegmentedAddress(0x1000, 0x0100)),
            CreateNop(new SegmentedAddress(0x1000, 0x0103)),
            CreateNop(new SegmentedAddress(0x1000, 0x0106))
        };

        using (CpuHeavyLogger logger = new(_dumpContext, logPath)) {
            // Act
            foreach (var node in nodes) {
                logger.LogInstruction(node);
            }
        }

        // Assert
        string logContent = File.ReadAllText(logPath);
        logContent.Should().Be("1000:0100 nop\n1000:0103 nop\n1000:0106 nop\n");
    }

    private Nop CreateNop(SegmentedAddress address) {
        var opcodeField = new InstructionField<ushort>(0, 1, address.Linear, 0xEB, ImmutableList.Create<byte?>(0x90), true);
        return new(address, opcodeField);
    }
}
