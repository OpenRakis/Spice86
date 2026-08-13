namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.CLI.RuntimeOptions;

using Xunit;

public class RuntimeOptionsMapperTests {
    [Fact]
    public void ToGdbServerOptions_MapsGdbPort() {
        // Arrange
        Configuration configuration = new() {
            GdbPort = 12345,
            Exe = "dummy.exe"
        };

        // Act
        GdbServerOptions options = RuntimeOptionsMapper.ToGdbServerOptions(configuration);

        // Assert
        options.Port.Should().Be(12345);
    }

    [Fact]
    public void ToExecutionPolicyOptions_MapsExecutionPolicyFields() {
        // Arrange
        Configuration configuration = new() {
            Debug = true,
            StopAfterCycles = 987654,
            GdbPort = 10000,
            Exe = "dummy.exe"
        };

        // Act
        ExecutionPolicyOptions options = RuntimeOptionsMapper.ToExecutionPolicyOptions(configuration);

        // Assert
        options.Debug.Should().BeTrue();
        options.StopAfterCycles.Should().Be(987654);
        options.GdbServer.Port.Should().Be(10000);
    }

    [Fact]
    public void CreateDosRuntimeState_MapsInitializeDosIntoInstallDosServices() {
        // Arrange
        Configuration configuration = new() {
            InitializeDOS = true,
            Exe = "dummy.exe"
        };

        // Act
        DosRuntimeState runtimeState = RuntimeOptionsMapper.CreateDosRuntimeState(configuration);

        // Assert
        runtimeState.InstallDosServices.Should().BeTrue();
    }

    [Fact]
    public void ToProgramLoadOptions_AndToMemoryDumpOptions_ShareDosRuntimeState() {
        // Arrange
        Configuration configuration = new() {
            Exe = "game.exe",
            ExeArgs = "-x",
            ExpectedChecksumValue = [1, 2, 3],
            CDrive = "C:\\GAMES",
            InitializeDOS = null
        };
        DosRuntimeState runtimeState = RuntimeOptionsMapper.CreateDosRuntimeState(configuration);

        // Act
        ProgramLoadOptions programLoadOptions = RuntimeOptionsMapper.ToProgramLoadOptions(configuration, runtimeState);
        MemoryDumpOptions memoryDumpOptions = RuntimeOptionsMapper.ToMemoryDumpOptions(runtimeState);

        // Assert
        programLoadOptions.Exe.Should().Be("game.exe");
        programLoadOptions.ExeArgs.Should().Be("-x");
        programLoadOptions.ExpectedChecksumValue.Should().Equal([1, 2, 3]);
        programLoadOptions.CDrive.Should().Be("C:\\GAMES");
        programLoadOptions.DosRuntimeState.Should().BeSameAs(runtimeState);
        memoryDumpOptions.DosRuntimeState.Should().BeSameAs(runtimeState);
    }
}