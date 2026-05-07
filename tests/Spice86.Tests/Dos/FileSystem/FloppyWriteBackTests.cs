namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Structures;

using System;
using System.IO;

using Xunit;

/// <summary>
/// Tests for floppy disk write-back (dirty tracking and flush-to-disk).
/// </summary>
public class FloppyWriteBackTests {
    [Fact]
    public void MarkDirty_SetsDirtyFlag() {
        // Arrange
        FloppyDiskDrive drive = CreateDriveWithImage();

        // Act
        drive.MarkDirty();

        // Assert
        drive.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_BeforeAnyWrite_IsFalse() {
        // Arrange & Act
        FloppyDiskDrive drive = CreateDriveWithImage();

        // Assert
        drive.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void FlushToDisk_WhenDirty_WritesImageToFile() {
        // Arrange
        string tempPath = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString() + ".img");
        try {
            byte[] imageData = new Fat12ImageBuilder().Build();
            FloppyDiskDrive drive = new FloppyDiskDrive { DriveLetter = 'A' };
            drive.MountImage(imageData, tempPath);
            drive.MarkDirty();

            // Act
            drive.FlushToDisk();

            // Assert
            File.Exists(tempPath).Should().BeTrue();
            byte[] written = File.ReadAllBytes(tempPath);
            written.Should().Equal(imageData);
            drive.IsDirty.Should().BeFalse();
        } finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void FlushToDisk_WhenNotDirty_DoesNotWriteFile() {
        // Arrange
        string tempPath = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString() + ".img");
        try {
            byte[] imageData = new Fat12ImageBuilder().Build();
            FloppyDiskDrive drive = new FloppyDiskDrive { DriveLetter = 'A' };
            drive.MountImage(imageData, tempPath);

            // Act (not dirty, do not call MarkDirty)
            drive.FlushToDisk();

            // Assert: file should not exist since we never flushed
            File.Exists(tempPath).Should().BeFalse();
        } finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void FlushToDisk_WithNoImagePath_DoesNotThrow() {
        // Arrange
        FloppyDiskDrive drive = new FloppyDiskDrive { DriveLetter = 'A' };
        drive.MarkDirty();

        // Act & Assert
        FluentActions.Invoking(() => drive.FlushToDisk()).Should().NotThrow();
    }

    private static FloppyDiskDrive CreateDriveWithImage() {
        byte[] imageData = new Fat12ImageBuilder().Build();
        FloppyDiskDrive drive = new FloppyDiskDrive { DriveLetter = 'A' };
        drive.MountImage(imageData, "test.img");
        return drive;
    }
}
