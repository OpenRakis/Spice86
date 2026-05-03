namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

using System.Text;

using Xunit;

/// <summary>
/// Tests for <see cref="DosDriveManager"/> floppy image mounting.
/// </summary>
public class DosDriveManagerFloppyTests {
    private readonly DosDriveManager _driveManager;

    public DosDriveManagerFloppyTests() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        _driveManager = new DosDriveManager(logger, "/tmp/test-c-drive", null);
    }

    [Fact]
    public void MountFloppyImage_OnDriveA_FloppyDriveIsAvailable() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();

        // Act
        _driveManager.MountFloppyImage('A', image);

        // Assert
        bool found = _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        found.Should().BeTrue();
        floppy.Should().NotBeNull();
        floppy!.HasImage.Should().BeTrue();
    }

    [Fact]
    public void MountFloppyImage_OnDriveA_LabelMatchesVolumeLabel() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();

        // Act
        _driveManager.MountFloppyImage('A', image);

        // Assert
        _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        floppy!.Label.Should().Be("TEST FLOPPY");
    }

    [Fact]
    public void TryGetFloppyDrive_WhenNotMounted_ReturnsFalse() {
        // Act
        bool found = _driveManager.TryGetFloppyDrive('A', out _);

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public void MountFloppyFolder_OnDriveA_RemovesAnyExistingImage() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        _driveManager.MountFloppyImage('A', image);

        // Act
        _driveManager.MountFloppyFolder('A', "/tmp");

        // Assert
        bool found = _driveManager.TryGetFloppyDrive('A', out _);
        found.Should().BeFalse();
    }

    [Fact]
    public void MountFloppyFolder_UpdatesVirtualDriveHostPath() {
        // Act
        _driveManager.MountFloppyFolder('A', "/tmp/floppy-root");

        // Assert
        _driveManager['A'].MountedHostDirectory.Should().Contain("floppy-root");
    }

    [Fact]
    public void FloppyDrives_ReflectsAllMountedFloppyImages() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();

        // Act
        _driveManager.MountFloppyImage('A', image);
        _driveManager.MountFloppyImage('B', image);

        // Assert
        _driveManager.FloppyDrives.Should().ContainKey('A');
        _driveManager.FloppyDrives.Should().ContainKey('B');
    }
}
