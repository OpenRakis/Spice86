namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

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
        _driveManager.MountFloppyImage('A', image, "test.img");

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
        _driveManager.MountFloppyImage('A', image, "test.img");

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
        _driveManager.MountFloppyImage('A', image, "test.img");

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
        _driveManager.MountFloppyImage('A', image, "test.img");
        _driveManager.MountFloppyImage('B', image, "test.img");

        // Assert
        _driveManager.FloppyDrives.Should().ContainKey('A');
        _driveManager.FloppyDrives.Should().ContainKey('B');
    }

    [Fact]
    public void MountFloppyImage_StoresImagePath() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();

        // Act
        _driveManager.MountFloppyImage('A', image, "/path/to/floppy1.img");

        // Assert
        _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        floppy!.ImagePath.Should().Be("/path/to/floppy1.img");
    }

    [Fact]
    public void AddFloppyImage_IncreasesImageCount() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        _driveManager.MountFloppyImage('A', image, "floppy1.img");

        // Act
        _driveManager.AddFloppyImage('A', image, "floppy2.img");

        // Assert
        _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        floppy!.ImageCount.Should().Be(2);
    }

    [Fact]
    public void SwapFloppyDiscs_WithTwoImages_AdvancesToSecond() {
        // Arrange
        byte[] image1 = new Fat12ImageBuilder().Build();
        byte[] image2 = new Fat12ImageBuilder().Build();
        _driveManager.MountFloppyImage('A', image1, "floppy1.img");
        _driveManager.AddFloppyImage('A', image2, "floppy2.img");

        // Act
        _driveManager.SwapFloppyDiscs();

        // Assert — ImagePath points to the second image
        _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        floppy!.ImagePath.Should().Be("floppy2.img");
    }

    [Fact]
    public void SwapFloppyDiscs_AfterLastImage_WrapsToFirst() {
        // Arrange
        byte[] image1 = new Fat12ImageBuilder().Build();
        byte[] image2 = new Fat12ImageBuilder().Build();
        _driveManager.MountFloppyImage('A', image1, "floppy1.img");
        _driveManager.AddFloppyImage('A', image2, "floppy2.img");

        // Act — swap twice to cycle back to first
        _driveManager.SwapFloppyDiscs();
        _driveManager.SwapFloppyDiscs();

        // Assert — wraps back to the first image
        _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        floppy!.ImagePath.Should().Be("floppy1.img");
    }

    [Fact]
    public void FloppyDiskDrive_HasMultipleImages_FalseForSingleImage() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        FloppyDiskDrive drive = new() { DriveLetter = 'A' };

        // Act
        drive.MountImage(image, "single.img");

        // Assert
        drive.ImageCount.Should().Be(1);
    }

    [Fact]
    public void FloppyDiskDrive_SwapToNextImage_NoEffectWithSingleImage() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        FloppyDiskDrive drive = new() { DriveLetter = 'A' };
        drive.MountImage(image, "single.img");

        // Act
        drive.SwapToNextImage();

        // Assert — still on the same image
        drive.ImagePath.Should().Be("single.img");
    }
}
