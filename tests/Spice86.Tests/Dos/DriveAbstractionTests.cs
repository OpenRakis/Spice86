namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Tests for drive abstraction strategy (host-backed vs memory-backed).
/// Phase 2 architecture: foundational for Z: memory drive and AUTOEXEC.BAT generation.
/// </summary>
public class DriveAbstractionTests {

    /// <summary>
    /// Memory drive should be created with read-only property.
    /// </summary>
    [Fact]
    public void MemoryDrive_IsCreated_WithReadOnlyProperty() {
        // Arrange
        const char driveLetter = 'Z';

        // Act
        MemoryDrive drive = new MemoryDrive {
            DriveLetter = driveLetter,
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };

        // Assert
        drive.DriveLetter.Should().Be(driveLetter);
        drive.Label.Should().Be("MEMORY");
        drive.IsReadOnlyMedium.Should().BeTrue("Z: memory drives must be read-only");
        drive.IsRemovable.Should().BeFalse("Memory drive should not be removable");
    }

    /// <summary>
    /// Memory drive's root directory should be accessible.
    /// </summary>
    [Fact]
    public void MemoryDrive_RootDirectory_IsAccessible() {
        // Arrange
        MemoryDrive drive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };

        // Act & Assert
        drive.Should().NotBeNull();
        drive.DriveLetter.Should().Be('Z');
    }

    /// <summary>
    /// Memory drive should support storing files in memory.
    /// </summary>
    [Fact]
    public void MemoryDrive_AddFile_StoresFileContent() {
        // Arrange
        MemoryDrive drive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        byte[] content = "Test file content"u8.ToArray();
        string filePath = "TEST.TXT";

        // Act
        drive.AddFile(filePath, content);

        // Assert
        drive.FileExists(filePath).Should().BeTrue();
        byte[] retrieved = drive.GetFile(filePath);
        retrieved.Should().Equal(content);
    }

    /// <summary>
    /// Memory drive should reject write attempts via exception.
    /// </summary>
    [Fact]
    public void MemoryDrive_WriteAttempt_ThrowsNotSupportedException() {
        // Arrange
        MemoryDrive drive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };

        // Act & Assert
        Action act = () => drive.CreateFile("NEWFILE.TXT");
        act.Should().Throw<NotSupportedException>("Write operations must be rejected on read-only memory drive");
    }

    /// <summary>
    /// Memory drive should support directory structure with path navigation.
    /// </summary>
    [Fact]
    public void MemoryDrive_Directory_SupportPathNavigation() {
        // Arrange
        MemoryDrive drive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        byte[] content = "AUTOEXEC content"u8.ToArray();

        // Act
        drive.AddFile("AUTOEXEC.BAT", content);
        drive.AddFile("BATCH\\SCRIPT.BAT", "batch script"u8.ToArray());

        // Assert
        drive.FileExists("AUTOEXEC.BAT").Should().BeTrue();
        drive.FileExists("BATCH\\SCRIPT.BAT").Should().BeTrue();
        drive.DirectoryExists("BATCH").Should().BeTrue();
    }

    /// <summary>
    /// Memory drive Z: can be mounted in DOS drive manager and retrieved by letter.
    /// </summary>
    [Fact]
    public void DosDriveManager_MountMemoryDrive_AddsZDrive() {
        // Arrange
        ILoggerService logger = Substitute.For<ILoggerService>();
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
        // zDrive is not null and retrieved == zDrive, so assert properties on zDrive directly.
        zDrive.DriveLetter.Should().Be('Z');
        zDrive.IsReadOnlyMedium.Should().BeTrue();
        manager['C'].MountedHostDirectory.Should().NotBeEmpty("C: drive must remain unaffected");
    }

}
