namespace Spice86.Tests.CfgCpu.Logging;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.Logging;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;

using Xunit;

public class CpuHeavyLoggerTests : IDisposable {
    private readonly EmulatorStateSerializationFolder _emulatorStateSerializationFolder = new(Path.GetTempPath());
    private readonly List<string> _filesToCleanup = new();
    private readonly State _state = new(CpuModel.INTEL_8086);

    private CpuHeavyLogger Create(string? logPath, AsmRenderingStyle style) {
        AsmRenderingConfig config = AsmRenderingConfig.Create(style);
        NodeToString nodeToString = new(config);
        return new(_emulatorStateSerializationFolder, logPath, nodeToString, _state, config);
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
        using CpuHeavyLogger logger = Create(null, AsmRenderingStyle.Spice86);

        // Assert
        File.Exists(expectedLogPath).Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithCustomPath_CreatesLogFileAtCustomLocation() {
        // Arrange
        string customLogPath = GetUniqueLogPath();

        // Act
        using CpuHeavyLogger logger = Create(customLogPath, AsmRenderingStyle.Spice86);

        // Assert
        File.Exists(customLogPath).Should().BeTrue();
    }

    [Fact]
    public void LogInstruction_SingleInstruction_WritesInstruction() {
        // Arrange
        string logPath = GetUniqueLogPath();
        
        // Set predictable register values for consistent test output
        _state.EAX = 0x00000000;
        _state.EBX = 0x00000000;
        _state.ECX = 0x000000FF;
        _state.EDX = 0x000001DD;
        _state.ESI = 0x00000100;
        _state.EDI = 0x0000FFFE;
        _state.EBP = 0x0000091C;
        _state.ESP = 0x0000FFFE;
        _state.DS = 0x01DD;
        _state.ES = 0x01DD;
        _state.SS = 0x01DD;
        _state.CarryFlag = false;
        _state.ZeroFlag = false;
        _state.SignFlag = false;
        _state.OverflowFlag = false;
        _state.InterruptFlag = true;
        _state.DirectionFlag = false;
        _state.TrapFlag = false;
        _state.AuxiliaryFlag = false;
        _state.ParityFlag = false;

        Nop nop = CreateNop(new SegmentedAddress(0x01DD, 0x0100));
        using (CpuHeavyLogger logger = Create(logPath, AsmRenderingStyle.DosBox)) {
            logger.LogInstruction(nop);
        }

        // Assert
        string[] logContent = File.ReadAllLines(logPath);
        logContent.Should().HaveCount(1);
        logContent[0].Should().Be("01DD:0100  nop                            EAX:00000000 EBX:00000000 ECX:000000FF EDX:000001DD ESI:00000100 EDI:0000FFFE EBP:0000091C ESP:0000FFFE DS:01DD ES:01DD SS:01DD C0 Z0 S0 O0 I1");
    }

    private Nop CreateNop(SegmentedAddress address) {
        var opcodeField = new InstructionField<ushort>(0, 1, address.Linear, 0xEB, ImmutableList.Create<byte?>(0x90), true);
        return new(address, opcodeField);
    }
}




