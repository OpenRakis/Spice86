namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using NSubstitute;

using Xunit;

/// <summary>
/// Tests for Z: drive mounting and AUTOEXEC.BAT bootstrap integration.
/// Phase 2 architecture: demonstrates polymorphic drive system and program dispatch.
/// </summary>
public class DosDriveManagerBootstrapTests {

    private static ILoggerService CreateMockLogger() => Substitute.For<ILoggerService>();

    /// <summary>
    /// RED TEST: DosDriveManager should support mounting MemoryDrive as Z: via helper method.
    /// </summary>
    [Fact]
    public void DosDriveManager_MountZDrive_WithMemoryDriveViaHelper() {
        // Arrange
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        DosDriveManager manager = new DosDriveManager(logger, tempCDir, null);

        MemoryDrive zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };

        // Act
        manager.MountMemoryDrive(zDrive);

        // Assert
        manager.TryGetMemoryDrive('Z', out MemoryDrive? retrieved).Should().BeTrue();
        retrieved.Should().Be(zDrive);
        retrieved!.DriveLetter.Should().Be('Z');
        retrieved.IsReadOnlyMedium.Should().BeTrue();
    }

    /// <summary>
    /// RED TEST: Z: drive should be accessible without affecting C: drive.
    /// </summary>
    [Fact]
    public void DosDriveManager_AddZDrive_DoesNotAffectCDrive() {
        // Arrange
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        DosDriveManager manager = new DosDriveManager(logger, tempCDir, null);
        var originalCDrive = manager['C'];

        MemoryDrive zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };

        // Act
        manager.MountMemoryDrive(zDrive);

        // Assert
        manager['C'].Should().Be(originalCDrive);
        manager['C'].MountedHostDirectory.Should().NotBeEmpty();
        manager.TryGetMemoryDrive('Z', out var z).Should().BeTrue();
        z!.IsReadOnlyMedium.Should().BeTrue();
    }

    /// <summary>
    /// RED TEST: AUTOEXEC.BAT should be generated and injected into Z: drive at bootstrap.
    /// </summary>
    [Fact]
    public void DosDriveManager_BootstrapZDrive_WithAutoexecBat() {
        // Arrange
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        string programPath = "C:\\PROGRAM.EXE";

        DosDriveManager manager = new DosDriveManager(logger, tempCDir, null);
        MemoryDrive zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        AutoexecBatGenerator generator = new AutoexecBatGenerator();
        byte[] autoexecContent = generator.Generate(programPath);

        // Act
        zDrive.AddFile("AUTOEXEC.BAT", autoexecContent);
        manager.MountMemoryDrive(zDrive);

        // Assert
        manager.TryGetMemoryDrive('Z', out var retrievedDrive).Should().BeTrue();
        retrievedDrive!.FileExists("AUTOEXEC.BAT").Should().BeTrue();
        byte[] retrieved = retrievedDrive.GetFile("AUTOEXEC.BAT");
        retrieved.Should().Equal(autoexecContent);

        // Verify content references the program
        string text = System.Text.Encoding.ASCII.GetString(retrieved);
        text.Should().Contain("PROGRAM.EXE");
    }

    /// <summary>
    /// RED TEST: Z: drive files should be retrievable via path lookup.
    /// </summary>
    [Fact]
    public void DosDriveManager_ResolveZDrivePath_AccessesMemoryDrive() {
        // Arrange
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        DosDriveManager manager = new DosDriveManager(logger, tempCDir, null);

        MemoryDrive zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        byte[] testContent = "Test batch file"u8.ToArray();
        zDrive.AddFile("BATCH\\TEST.BAT", testContent);
        manager.MountMemoryDrive(zDrive);

        // Act
        var retrieved = manager.TryGetMemoryDrive('Z', out MemoryDrive? retrievedDrive);

        // Assert
        retrieved.Should().BeTrue();
        retrievedDrive.Should().Be(zDrive);
        retrievedDrive!.FileExists("BATCH\\TEST.BAT").Should().BeTrue();
        byte[] content = retrievedDrive.GetFile("BATCH\\TEST.BAT");
        content.Should().Equal(testContent);
    }

    /// <summary>
    /// RED TEST: Multiple programs should be able to reference Z:\AUTOEXEC.BAT.
    /// </summary>
    [Fact]
    public void DosDriveManager_MultiplePrograms_ShareZDriveContent() {
        // Arrange
        ILoggerService logger = CreateMockLogger();
        string tempCDir = System.IO.Path.GetTempPath();
        DosDriveManager manager = new DosDriveManager(logger, tempCDir, null);

        MemoryDrive zDrive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };

        AutoexecBatGenerator generator = new AutoexecBatGenerator();
        string program1 = "C:\\PROG1.EXE";
        string program2 = "C:\\PROG2.EXE";

        byte[] autoexec1 = generator.Generate(program1);
        byte[] autoexec2 = generator.Generate(program2);

        // Act
        zDrive.AddFile("AUTOEXEC.BAT", autoexec1);
        manager.MountMemoryDrive(zDrive);

        // First program executes
        manager.TryGetMemoryDrive('Z', out MemoryDrive? batch1Drive).Should().BeTrue();
        byte[] batch1 = batch1Drive!.GetFile("AUTOEXEC.BAT");

        // Update for second program (simulating bootstrap for different executable)
        zDrive.AddFile("AUTOEXEC.BAT", autoexec2);
        manager.TryGetMemoryDrive('Z', out MemoryDrive? batch2Drive).Should().BeTrue();
        byte[] batch2 = batch2Drive!.GetFile("AUTOEXEC.BAT");

        // Assert
        batch1.Should().NotEqual(batch2);
        System.Text.Encoding.ASCII.GetString(batch1).Should().Contain("PROG1.EXE");
        System.Text.Encoding.ASCII.GetString(batch2).Should().Contain("PROG2.EXE");
    }
}
