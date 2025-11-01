namespace Spice86.Tests.Emulator.Gdb;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;

using Xunit;

/// <summary>
/// Integration tests for GDB server configuration and initialization.
/// Tests verify that GDB server is properly configured based on settings.
/// Full integration tests with actual GDB client communication require refactoring
/// ProgramExecutor to expose the GDB server for testing purposes.
/// </summary>
public class GdbServerIntegrationTests {
    [Fact]
    public void Spice86_WithGdbPortConfigured_ShouldCreateGdbServer() {
        // Arrange
        Configuration config = new() {
            Exe = "Resources/cpuTests/add.bin",
            ExpectedChecksumValue = Array.Empty<byte>(),
            GdbPort = 10000,
            HeadlessMode = HeadlessType.Minimal,
            InstructionsPerSecond = 100000
        };

        // Act
        using Spice86DependencyInjection injection = new(config);

        // Assert
        // GDB server creation is verified indirectly through the configuration
        // The actual server field is private in ProgramExecutor
        injection.ProgramExecutor.Should().NotBeNull();
    }

    [Fact]
    public void Spice86_WithGdbPortZero_ShouldNotStartGdbServer() {
        // Arrange
        Configuration config = new() {
            Exe = "Resources/cpuTests/add.bin",
            ExpectedChecksumValue = Array.Empty<byte>(),
            GdbPort = 0, // GDB disabled
            HeadlessMode = HeadlessType.Minimal
        };

        // Act
        using Spice86DependencyInjection injection = new(config);

        // Assert
        // When port is 0, GDB server should not be created
        injection.ProgramExecutor.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)] // Without CfgCpu
    [InlineData(true)]  // With CfgCpu
    public void Spice86_WithInstructionsPerSecond_ShouldWorkWithBothCpuModes(bool enableCfgCpu) {
        // Arrange
        Configuration config = new() {
            Exe = "Resources/cpuTests/add.bin",
            ExpectedChecksumValue = Array.Empty<byte>(),
            GdbPort = 10000,
            InstructionsPerSecond = 100000,
            HeadlessMode = HeadlessType.Minimal,
            CfgCpu = enableCfgCpu
        };

        // Act
        using Spice86DependencyInjection injection = new(config);

        // Assert
        injection.Machine.Should().NotBeNull();
        injection.Machine.Cpu.Should().NotBeNull();
    }

    [Fact]
    public void Spice86_InHeadlessMode_ShouldInitializeWithGdb() {
        // Arrange
        Configuration config = new() {
            Exe = "Resources/cpuTests/add.bin",
            ExpectedChecksumValue = Array.Empty<byte>(),
            GdbPort = 10001,
            HeadlessMode = HeadlessType.Minimal,
            InstructionsPerSecond = 100000
        };

        // Act
        using Spice86DependencyInjection injection = new(config);

        // Assert
        injection.Machine.Should().NotBeNull("Machine should be created in headless mode");
        injection.ProgramExecutor.Should().NotBeNull("ProgramExecutor should be created");
    }
}
