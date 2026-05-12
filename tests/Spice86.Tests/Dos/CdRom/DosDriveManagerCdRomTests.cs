namespace Spice86.Tests.Dos.CdRom;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Interfaces;

using Spice86.Tests.Dos;

using Xunit;

/// <summary>
/// Tests for CD-ROM drive registration in <see cref="DosDriveManager"/>.
/// </summary>
public class DosDriveManagerCdRomTests {
    private readonly DosDriveManager _driveManager;

    public DosDriveManagerCdRomTests() {
        ILoggerService logger = Substitute.For<ILoggerService>();
        _driveManager = DosTestHelpers.CreateDriveManager(logger, "/tmp/test-c-drive", null);
    }

    [Fact]
    public void RegisterCdRomDriveLetter_NewLetter_DriveExistsInMap() {
        // Act
        _driveManager.RegisterCdRomDriveLetter('D', string.Empty);

        // Assert
        _driveManager.TryGetValue('D', out _).Should().BeTrue();
    }

    [Fact]
    public void RegisterCdRomDriveLetter_WithHostPath_MountedHostDirectoryIsSet() {
        // Act
        _driveManager.RegisterCdRomDriveLetter('D', "/some/folder");

        // Assert
        _driveManager.TryGetValue('D', out Spice86.Core.Emulator.OperatingSystem.Structures.VirtualDrive? drive).Should().BeTrue();
        drive!.MountedHostDirectory.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RegisterCdRomDriveLetter_EmptyHostPath_DriveExistsButNoHostDirectory() {
        // Act
        _driveManager.RegisterCdRomDriveLetter('E', string.Empty);

        // Assert
        _driveManager.TryGetValue('E', out Spice86.Core.Emulator.OperatingSystem.Structures.VirtualDrive? drive).Should().BeTrue();
        drive!.MountedHostDirectory.Should().BeNullOrEmpty();
    }

    [Fact]
    public void RegisterCdRomDriveLetter_ExistingLetter_OverwritesEntry() {
        // Arrange – pre-register D: with some path
        _driveManager.RegisterCdRomDriveLetter('D', "/first/path");

        // Act – register again with a different path
        _driveManager.RegisterCdRomDriveLetter('D', "/second/path");

        // Assert – new path wins
        _driveManager.TryGetValue('D', out Spice86.Core.Emulator.OperatingSystem.Structures.VirtualDrive? drive).Should().BeTrue();
        drive!.MountedHostDirectory.Should().Contain("second");
    }
}
