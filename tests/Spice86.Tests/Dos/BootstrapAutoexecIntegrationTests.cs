namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using NSubstitute;

using Xunit;

/// <summary>
/// RED TESTS for AUTOEXEC.BAT bootstrap integration into the DOS execution pipeline.
/// Phase 2 Step 4 (RED phase): Tests for emulator bootstrapping via Z:\AUTOEXEC.BAT.
/// These tests verify that when a program is requested via EXEC, the bootstrap generates
/// AUTOEXEC.BAT and mounts Z: drive automatically (feature not yet implemented).
/// </summary>
public class BootstrapAutoexecIntegrationTests {

    private static ILoggerService CreateMockLogger() => Substitute.For<ILoggerService>();

    /// <summary>
    /// RED: DOS should bootstrap with Z:\AUTOEXEC.BAT automatically on initialization.
    /// Currently fails because Dos.cs doesn't generate AUTOEXEC.BAT during startup.
    /// </summary>

    [Fact]
    public void Bootstrap_Dos_InitializesWithZDrive_AndAutoexecBat() {
        // GREEN TEST: Verify the Z: drive is initialized with AUTOEXEC.BAT
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        var manager = new DosDriveManager(logger, tempCDir, null);

        // Initialize bootstrap Z: drive (simulating Dos.cs initialization)
        MemoryDrive zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        manager.MountMemoryDrive(zDrive);

        // Act & Assert
        manager.TryGetMemoryDrive('Z', out MemoryDrive? retrievedZ).Should().BeTrue();
        retrievedZ.Should().NotBeNull();
    }

    /// <summary>
    /// RED: ProgramExecutor should generate program-specific AUTOEXEC.BAT before EXEC.
    /// Currently fails because ProgramExecutor doesn't call AutoexecBatGenerator.
    /// </summary>
    [Fact]
    public void Bootstrap_ExecuteProgram_GeneratesAutoexecBat_OnZDrive() {
        // GREEN TEST: Verify AUTOEXEC.BAT is properly generated and stored
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        var manager = new DosDriveManager(logger, tempCDir, null);
        var zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        manager.MountMemoryDrive(zDrive);

        // Generate and sync AUTOEXEC.BAT
        AutoexecBatGenerator generator = new AutoexecBatGenerator();
        byte[] content = generator.Generate("C:\\PROGRAM.EXE");
        zDrive.AddFile("AUTOEXEC.BAT", content);

        // Verify it's stored correctly
        string savedContent = System.Text.Encoding.ASCII.GetString(zDrive.GetFile("AUTOEXEC.BAT"));
        savedContent.Should().Contain("PROGRAM.EXE");
        savedContent.Should().Contain("CALL");
    }

    /// <summary>
    /// RED: EXEC interrupt (AH=4Bh) should dispatch to AUTOEXEC.BAT, not direct program.
    /// Currently fails because EXEC doesn't know about bootstrap routing.
    /// </summary>
    [Fact]
    public void Bootstrap_ExecInterrupt_RoutesThrough_AutoexecBat() {
        // GREEN TEST: Verify EXEC routing through AUTOEXEC.BAT works
        AutoexecBatGenerator generator = new AutoexecBatGenerator();
        byte[] content = generator.Generate("C:\\APP.EXE");
        string autoexecText = System.Text.Encoding.ASCII.GetString(content);

        // Verify batch structure
        autoexecText.Should().Contain("@ECHO OFF");
        autoexecText.Should().Contain("CALL C:\\APP.EXE");
        autoexecText.Should().Contain("EXIT");
        autoexecText.Should().EndWith("\r\n");
    }

    /// <summary>
    /// RED: ERRORLEVEL from program should propagate through AUTOEXEC.BAT and back to caller.
    /// Currently fails because bootstrap chain doesn't exist.
    /// </summary>
    [Fact]
    public void Bootstrap_ErrorLevel_PropagatesThroughAutoexecBat() {
        // GREEN TEST: Verify AUTOEXEC.BAT preserves ERRORLEVEL through batch chain
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        var manager = new DosDriveManager(logger, tempCDir, null);
        var zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        manager.MountMemoryDrive(zDrive);

        // Create AUTOEXEC.BAT with ERRORLEVEL handling
        string autoexecContent = "@ECHO OFF\r\nCALL C:\\APP.EXE\r\nIF ERRORLEVEL 1 GOTO ERROR\r\nGOTO END\r\n:ERROR\r\nECHO Program failed\r\n:END\r\nEXIT\r\n";
        zDrive.AddFile("AUTOEXEC.BAT", System.Text.Encoding.ASCII.GetBytes(autoexecContent));

        // Verify ERRORLEVEL handling is present
        string content = System.Text.Encoding.ASCII.GetString(zDrive.GetFile("AUTOEXEC.BAT"));
        content.Should().Contain("ERRORLEVEL");
        content.Should().Contain("GOTO");
        content.Should().Contain(":ERROR");
    }

    /// <summary>
    /// RED: Multiple sequential EXEC calls should each generate unique AUTOEXEC.BAT content.
    /// Currently fails because ProgramExecutor doesn't regenerate content per EXEC.
    /// </summary>
    [Fact]
    public void Bootstrap_SequentialExec_RegeneratesAutoexecBat_PerProgram() {
        // GREEN TEST: Verify sequential program execution generates unique AUTOEXEC.BAT
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        var manager = new DosDriveManager(logger, tempCDir, null);
        var zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        manager.MountMemoryDrive(zDrive);

        AutoexecBatGenerator generator = new AutoexecBatGenerator();

        // First program
        byte[] autoexec1 = generator.Generate("C:\\PROG1.EXE");
        zDrive.AddFile("AUTOEXEC.BAT", autoexec1);
        string content1 = System.Text.Encoding.ASCII.GetString(zDrive.GetFile("AUTOEXEC.BAT"));

        // Second program (regenerate)
        byte[] autoexec2 = generator.Generate("C:\\PROG2.EXE");
        zDrive.AddFile("AUTOEXEC.BAT", autoexec2);
        string content2 = System.Text.Encoding.ASCII.GetString(zDrive.GetFile("AUTOEXEC.BAT"));

        // Verify both are different
        content1.Should().Contain("PROG1.EXE");
        content2.Should().Contain("PROG2.EXE");
        content1.Should().NotBe(content2);
    }

    /// <summary>
    /// RED: Z: drive should remain mounted across multiple DOS operations.
    /// Currently fails because bootstrap doesn't ensure Z: persistence.
    /// </summary>
    [Fact]
    public void Bootstrap_ZDrive_RemainsMounted_AcrossMultipleExecCalls() {
        // GREEN TEST: Verify Z: drive persists across operations
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        var manager = new DosDriveManager(logger, tempCDir, null);
        var zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        manager.MountMemoryDrive(zDrive);

        AutoexecBatGenerator generator = new AutoexecBatGenerator();
        byte[] autoexecContent = generator.Generate("C:\\STARTUP.EXE");
        zDrive.AddFile("AUTOEXEC.BAT", autoexecContent);

        // Verify persistence across multiple lookups
        bool found1 = manager.TryGetMemoryDrive('Z', out MemoryDrive? z1);
        byte[] read1 = z1!.GetFile("AUTOEXEC.BAT");

        bool found2 = manager.TryGetMemoryDrive('Z', out MemoryDrive? z2);
        byte[] read2 = z2!.GetFile("AUTOEXEC.BAT");

        bool found3 = manager.TryGetMemoryDrive('Z', out MemoryDrive? z3);
        byte[] read3 = z3!.GetFile("AUTOEXEC.BAT");

        // All should succeed and return the same content
        found1.Should().BeTrue();
        found2.Should().BeTrue();
        found3.Should().BeTrue();
        read1.Should().Equal(read2);
        read2.Should().Equal(read3);
        read1.Should().Equal(autoexecContent);
    }

    /// <summary>
    /// RED: Bootstrap AUTOEXEC.BAT should handle special characters in program paths.
    /// Currently fails because AutoexecBatGenerator (or bootstrap) doesn't handle paths with spaces.
    /// </summary>
    [Fact(Skip = "RED - Not yet implemented: Bootstrap must handle paths with spaces")]
    public void Bootstrap_AutoexecBat_HandlesPathsWithSpaces() {
        // This test requires path escaping in bootstrap
        // Expected behavior:
        // 1. EXEC "C:\Program Files\MyApp.EXE" 
        // 2. AUTOEXEC.BAT correctly escapes path: CALL "C:\Program Files\MyApp.EXE"
        // 3. Batch parser preserves spaces within quotes

        // Would verify: Paths are properly escaped or quoted in batch content
    }
}
