namespace Spice86.Tests.CfgCpu.Logging;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;

using Xunit;

public class CpuHeavyLoggerTests : IDisposable {
    private readonly EmulatorStateSerializationFolder _emulatorStateSerializationFolder;
    private readonly List<string> _filesToCleanup = new();

    public CpuHeavyLoggerTests() {
        _emulatorStateSerializationFolder = new(Path.GetTempPath());
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
        string expectedLogPath = Path.Join(_emulatorStateSerializationFolder.Folder, "cpu_heavy.log");
        _filesToCleanup.Add(expectedLogPath);

        // Act
        using CpuHeavyLogger logger = new(_emulatorStateSerializationFolder, null);

        // Assert
        File.Exists(expectedLogPath).Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithCustomPath_CreatesLogFileAtCustomLocation() {
        // Arrange
        string customLogPath = GetUniqueLogPath();

        // Act
        using CpuHeavyLogger logger = new(_emulatorStateSerializationFolder, customLogPath);

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

        using (CpuHeavyLogger logger = new(_emulatorStateSerializationFolder, logPath)) {
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
