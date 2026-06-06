namespace Spice86.Tests.Dos;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Tests.Utility;

using System;

using Xunit;

/// <summary>
/// Tests for the SUBST internal batch command.
/// <c>SUBST</c> behaviour: substitute a drive letter for a path, remove an
/// existing SUBST with <c>/D</c>, or list active SUBSTs when invoked with
/// no arguments.
/// </summary>
public sealed class SubstBatchCommandTests : IDisposable {
    private readonly TempFile _tempFile;
    private readonly DosTestFixture _fixture;

    public SubstBatchCommandTests() {
        _tempFile = new TempFile("SubstTest");
        _fixture = new DosTestFixture(_tempFile.Path);
    }

    public void Dispose() {
        _fixture.Dispose();
        _tempFile.Dispose();
    }

    [Fact]
    public void Subst_WithDosPath_CreatesVirtualDrive() {
        string subFolder = Path.Join(_tempFile.Path, "games");
        Directory.CreateDirectory(subFolder);

        bool launched = _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine(
            "SUBST D: C:\\games", out _);

        launched.Should().BeFalse();
        _fixture.DriveManager.TryGetDrive<VirtualDrive>('D', out VirtualDrive? drive).Should().BeTrue();
        if (drive == null) {
            throw new InvalidOperationException("Expected D: drive to be mounted.");
        }
        drive.MountedHostDirectory.Should().Contain("games");
        _fixture.DriveManager.IsSubstDrive('D').Should().BeTrue();
    }

    [Fact]
    public void Subst_WithMissingPath_DoesNotCreateDrive() {
        bool launched = _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine(
            "SUBST D: C:\\does_not_exist", out _);

        launched.Should().BeFalse();
        _fixture.DriveManager.TryGetValue('D', out _).Should().BeFalse();
    }

    [Fact]
    public void Subst_WithSlashD_RemovesSubstDrive() {
        string subFolder = Path.Join(_tempFile.Path, "games");
        Directory.CreateDirectory(subFolder);
        _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine("SUBST D: C:\\games", out _);

        bool launched = _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine(
            "SUBST D: /D", out _);

        launched.Should().BeFalse();
        _fixture.DriveManager.IsSubstDrive('D').Should().BeFalse();
        _fixture.DriveManager.TryGetValue('D', out _).Should().BeFalse();
    }

    [Fact]
    public void Subst_SlashDOnNonSubstDrive_FailsGracefully() {
        bool launched = _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine(
            "SUBST E: /D", out _);

        launched.Should().BeFalse();
    }

    [Fact]
    public void Subst_OnFloppyLetter_Rejected() {
        string subFolder = Path.Join(_tempFile.Path, "games");
        Directory.CreateDirectory(subFolder);

        _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine("SUBST A: C:\\games", out _);

        _fixture.DriveManager.IsSubstDrive('A').Should().BeFalse();
    }

    [Fact]
    public void Subst_OnDriveAlreadyMounted_Rejected() {
        // C: is already mounted to _tempFile.Path by DosTestFixture.
        string subFolder = Path.Join(_tempFile.Path, "games");
        Directory.CreateDirectory(subFolder);

        _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine("SUBST C: C:\\games", out _);

        _fixture.DriveManager.IsSubstDrive('C').Should().BeFalse();
    }

    [Fact]
    public void Subst_NoArguments_ListsActiveSubsts() {
        string subFolder = Path.Join(_tempFile.Path, "games");
        Directory.CreateDirectory(subFolder);
        _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine("SUBST D: C:\\games", out _);

        bool launched = _fixture.ProcessManager.BatchExecutionEngine.TryExecuteCommandLine("SUBST", out _);

        launched.Should().BeFalse();
        _fixture.DriveManager.SubstDrives.Should().ContainKey('D');
    }

    [Fact]
    public void DosDriveManager_MountSubstDrive_TracksDriveAndOriginalPath() {
        _fixture.DriveManager.MountSubstDrive('E', _tempFile.Path, "C:\\GAMES");

        _fixture.DriveManager.IsSubstDrive('E').Should().BeTrue();
        _fixture.DriveManager.SubstDrives['E'].Should().Be("C:\\GAMES");
    }

    [Fact]
    public void DosDriveManager_UnmountSubstDrive_RemovesEntry() {
        _fixture.DriveManager.MountSubstDrive('E', _tempFile.Path, "C:\\GAMES");

        bool removed = _fixture.DriveManager.UnmountSubstDrive('E');

        removed.Should().BeTrue();
        _fixture.DriveManager.IsSubstDrive('E').Should().BeFalse();
        _fixture.DriveManager.TryGetValue('E', out _).Should().BeFalse();
    }

    [Fact]
    public void DosDriveManager_UnmountSubstDrive_OnNonSubstDrive_ReturnsFalse() {
        bool removed = _fixture.DriveManager.UnmountSubstDrive('Z');

        removed.Should().BeFalse();
    }
}
