namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.AsmFixtures;
using Spice86.Tests.Dos.FileSystem;
using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Thin theory wrapper running every batch routing scenario through the shared ASM pipeline.
/// The scenarios themselves are defined in <see cref="DosBatchScenarioCatalog.BatchScenarios"/>.
/// </summary>
public class DosBatchRoutingIntegrationTests {
    /// <summary>Identities of every batch routing scenario.</summary>
    public static IEnumerable<object[]> BatchScenarioIds =>
        DosBatchScenarioCatalog.BatchScenarios.Select(scenario => new object[] { scenario.Identity });

    /// <summary>
    /// Runs a single batch routing scenario in interpreted mode.
    /// </summary>
    /// <param name="scenarioIdentity">Identity of the scenario to run.</param>
    [Theory]
    [MemberData(nameof(BatchScenarioIds))]
    public void RunBatchScenario_Interpreted(string scenarioIdentity) {
        AsmScenario scenario = AsmScenarioCatalog.DosScenario(scenarioIdentity);
        AsmFixtureRunner.RunScenario(scenario, JitMode.InterpretedOnly);
    }

    /// <summary>
    /// The BOOT command loads the boot sector of a mounted floppy image at 0000:7C00 with DL=0.
    /// This test checks the initial loading state immediately after Create(), without executing the CPU.
    /// This is an initialization-only test, not an execution test.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_BootA_AutoLoadsMountedFloppyBootSector() {
        using TempFile tempFile = new("dos_batch_boot_program");
        string tempDir = tempFile.Path;

        // Arrange
        byte[] imageData = new Fat12ImageBuilder().Build();
        imageData[0x40] = 0xDE;
        imageData[0x41] = 0xAD;
        imageData[0x42] = 0xBE;
        imageData[0x43] = 0xEF;
        imageData[510] = 0x55;
        imageData[511] = 0xAA;

        string imagePath = Path.Join(tempDir, "BOOT.IMG");
        File.WriteAllBytes(imagePath, imageData);
        string startBatchPath = Path.Join(tempDir, "START.BAT");
        File.WriteAllText(startBatchPath,
            "imgmount a \"C:\\BOOT.IMG\" -t floppy\r\n" +
            "boot a:\r\n");

        using Spice86Creator creator = new(
            binName: startBatchPath,
            enablePit: false,
            maxCycles: 1000L,
            installInterruptVectors: true,
            cDrive: tempDir);
        using Spice86DependencyInjection spice86 = creator.Create();

        // Assert: check the initial loading state without running the program
        spice86.Machine.CpuState.CS.Should().Be(0, "the boot sector starts at 0000:7C00");
        spice86.Machine.CpuState.IP.Should().Be(0x7C00, "the boot sector starts at 0000:7C00");
        spice86.Machine.CpuState.DL.Should().Be(0x00, "the boot sector is entered with DL holding the boot drive");
        spice86.Machine.Memory.UInt8[0x7C00 + 0x40].Should().Be(0xDE, "the mounted boot image must be the one loaded at 0000:7C00");
        spice86.Machine.Memory.UInt8[0x7C00 + 0x41].Should().Be(0xAD, "the mounted boot image must be the one loaded at 0000:7C00");
        spice86.Machine.Memory.UInt8[0x7C00 + 0x42].Should().Be(0xBE, "the mounted boot image must be the one loaded at 0000:7C00");
        spice86.Machine.Memory.UInt8[0x7C00 + 0x43].Should().Be(0xEF, "the mounted boot image must be the one loaded at 0000:7C00");
    }

    /// <summary>
    /// A batch file that only runs internal commands is finished by the DOS batch engine while the program loads, so the
    /// emulation loop must not parse or execute anything at the root COMMAND.COM program area.
    /// </summary>
    [Fact]
    public void HostRequestedBatch_WithOnlyInternalCommands_ParsesNoInstruction() {
        using TempFile tempFile = new("dos_batch_internal_only");
        string tempDir = tempFile.Path;

        // Arrange
        string startBatchPath = Path.Join(tempDir, "START.BAT");
        File.WriteAllText(startBatchPath, "VER > OUT.TXT\r\n");
        using Spice86Creator creator = new(
            binName: startBatchPath,
            enablePit: false,
            maxCycles: 1000L,
            installInterruptVectors: true,
            cDrive: tempDir);
        using Spice86DependencyInjection spice86 = creator.Create();
        spice86.Machine.CpuState.IsRunning.Should().BeFalse("the batch engine ran VER while loading and launched nothing");

        // Act
        spice86.ProgramExecutor.Run();

        // Assert
        spice86.Machine.CfgCpu.ExecutionContextManager.ExecutionContextEntryPoints.Keys.Should().NotContain(
            new SegmentedAddress(DosProcessManager.CommandComSegment, 0x0100),
            "nothing runs, so the root COMMAND.COM program area must not become an entry point");
        File.ReadAllText(Path.Join(tempDir, "OUT.TXT")).Should().NotBeEmpty("VER wrote its output through the redirection");
    }
}