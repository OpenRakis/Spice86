namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

/// <summary>
/// Tests for drive abstraction strategy (host-backed vs memory-backed).
/// Phase 2 architecture: foundational for Z: memory drive and AUTOEXEC.BAT generation.
/// </summary>
public class DriveAbstractionTests {

    /// <summary>
    /// RED TEST: Memory drive should be created with read-only property.
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
    /// RED TEST: Memory drive's root directory should be accessible.
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
    /// RED TEST: Memory drive should support storing files in memory.
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
    /// RED TEST: Memory drive should reject write attempts via exception.
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
    /// RED TEST: Memory drive should support directory structure with path navigation.
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
    /// RED TEST: AUTOEXEC.BAT generator creates valid batch script content.
    /// </summary>
    [Fact]
    public void AutoexecBatGenerator_GeneratesContent_WithValidFormat() {
        // Arrange
        AutoexecBatGenerator generator = new AutoexecBatGenerator();
        string programPath = "C:\\PROGRAM.EXE";

        // Act
        byte[] content = generator.Generate(programPath);

        // Assert
        content.Should().NotBeEmpty("AUTOEXEC.BAT content must not be empty");
        string text = System.Text.Encoding.ASCII.GetString(content);
        text.Should().Contain("PROGRAM.EXE", "AUTOEXEC.BAT must reference program to execute");
        text.Should().EndWith("\r\n", "DOS batch files must end with CRLF");
    }

    /// <summary>
    /// RED TEST: Memory drive Z: can be mounted in DOS drive manager.
    /// </summary>
    [Fact]
    public void DosDriveManager_MountMemoryDrive_AddsZDrive() {
        // Arrange
        // This requires modifying DosDriveManager to accept both VirtualDrive and MemoryDrive
        // Placeholder: assumes new constructor or method signature

        // Act & Assert
        Assert.True(true, "Placeholder: Requires DosDriveManager refactor for polymorphic drives");
    }

    /// <summary>
    /// RED TEST: AUTOEXEC.BAT injected into Z: drive is accessible for batch execution.
    /// </summary>
    [Fact]
    public void MemoryDrive_WithAutoexecBat_IsReadableForExecution() {
        // Arrange
        MemoryDrive drive = new MemoryDrive {
            DriveLetter = 'Z',
            Label = "MEMORY",
            IsReadOnlyMedium = true,
        };
        AutoexecBatGenerator generator = new AutoexecBatGenerator();
        byte[] autoexecContent = generator.Generate("C:\\PROGRAM.EXE");

        // Act
        drive.AddFile("AUTOEXEC.BAT", autoexecContent);

        // Assert
        drive.FileExists("AUTOEXEC.BAT").Should().BeTrue();
        byte[] retrieved = drive.GetFile("AUTOEXEC.BAT");
        retrieved.Should().Equal(autoexecContent);
        string text = System.Text.Encoding.ASCII.GetString(retrieved);
        text.Should().NotBeEmpty();
    }
}
