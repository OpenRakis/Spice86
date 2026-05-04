namespace Spice86.Tests.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Batch;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Dos.FileSystem;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

/// <summary>
/// Tests for the MOUNT and IMGMOUNT internal batch commands.
/// Uses an in-memory batch execution context so no actual machine startup is needed.
/// </summary>
public class MountBatchCommandTests : IDisposable {
    private readonly string _tempDir;
    private readonly DosDriveManager _driveManager;
    private readonly MscdexService _mscdex;
    private readonly DosBatchExecutionEngineAccessor _accessor;
    private readonly StringBuilder _output;

    public MountBatchCommandTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MountTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        ILoggerService logger = Substitute.For<ILoggerService>();
        _driveManager = DosTestHelpers.CreateDriveManager(logger, _tempDir, null);
        _output = new StringBuilder();
        _accessor = new DosBatchExecutionEngineAccessor(_driveManager, _output);

        // Create a mock State + Memory for MscdexService
        IMemory memory = Substitute.For<IMemory>();
        Spice86.Core.Emulator.CPU.State state = new(Spice86.Core.Emulator.CPU.CpuModel.INTEL_80386);
        _mscdex = new MscdexService(state, memory, logger);
    }

    public void Dispose() {
        try {
            Directory.Delete(_tempDir, recursive: true);
        } catch (IOException) {
            // Ignore cleanup errors.
        }
    }

    [Fact]
    public void TryHandleMount_WithValidFolder_MountsDrive() {
        // Arrange
        string subFolder = Path.Combine(_tempDir, "floppy");
        Directory.CreateDirectory(subFolder);

        // Act
        _accessor.TryHandleMount($"A {subFolder}");

        // Assert
        _driveManager['A'].MountedHostDirectory.Should().Contain("floppy");
    }

    [Fact]
    public void TryHandleMount_WithMissingFolder_WritesErrorMessage() {
        // Arrange
        string missing = Path.Combine(_tempDir, "does-not-exist");

        // Act
        _accessor.TryHandleMount($"D {missing}");

        // Assert
        _output.ToString().Should().Contain("path not found");
    }

    [Fact]
    public void TryHandleMount_WithNoArguments_WritesUsageMessage() {
        // Act
        _accessor.TryHandleMount(string.Empty);

        // Assert
        _output.ToString().Should().Contain("Usage");
    }

    [Fact]
    public void TryHandleMount_WithFloppyType_MountsFolderToFloppyDrive() {
        // Arrange
        string subFolder = Path.Combine(_tempDir, "flp");
        Directory.CreateDirectory(subFolder);

        // Act
        _accessor.TryHandleMount($"A {subFolder} -t floppy");

        // Assert
        _driveManager['A'].MountedHostDirectory.Should().Contain("flp");
    }

    [Fact]
    public void TryHandleImgMount_WithValidFloppyImage_MountsFloppyDrive() {
        // Arrange
        byte[] imageBytes = new Fat12ImageBuilder().Build();
        string imagePath = Path.Combine(_tempDir, "floppy.img");
        File.WriteAllBytes(imagePath, imageBytes);

        // Act
        _accessor.TryHandleImgMount($"A {imagePath} -t floppy", _mscdex);

        // Assert
        bool mounted = _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        mounted.Should().BeTrue();
        floppy!.HasImage.Should().BeTrue();
    }

    [Fact]
    public void TryHandleImgMount_WithMissingImage_WritesErrorMessage() {
        // Arrange
        string missing = Path.Combine(_tempDir, "missing.img");

        // Act
        _accessor.TryHandleImgMount($"A {missing} -t floppy", _mscdex);

        // Assert
        _output.ToString().Should().Contain("not found");
    }

    [Fact]
    public void TryHandleImgMount_WithNoArguments_WritesUsageMessage() {
        // Act
        _accessor.TryHandleImgMount(string.Empty, _mscdex);

        // Assert
        _output.ToString().Should().Contain("Usage");
    }

    [Fact]
    public void TryHandleImgMount_AutoDetectFloppyByExtension_MountsImage() {
        // Arrange — .img extension should auto-detect as floppy
        byte[] imageBytes = new Fat12ImageBuilder().Build();
        string imagePath = Path.Combine(_tempDir, "disk.img");
        File.WriteAllBytes(imagePath, imageBytes);

        // Act — no -t flag, let extension detection work
        _accessor.TryHandleImgMount($"A {imagePath}", _mscdex);

        // Assert
        bool mounted = _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        mounted.Should().BeTrue();
        floppy!.HasImage.Should().BeTrue();
    }

    [Fact]
    public void TryHandleMount_WithRelativePath_ResolvedAgainstCwd() {
        // Arrange — create a subfolder inside _tempDir
        string subFolder = Path.Combine(_tempDir, "reltest");
        Directory.CreateDirectory(subFolder);

        // Compute a relative path from the process CWD to the subfolder
        string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, subFolder);

        // Act
        _accessor.TryHandleMount($"A {relativePath}");

        // Assert — the drive should be mounted even though the path was relative
        _driveManager['A'].MountedHostDirectory.Should().NotBeNullOrWhiteSpace();
        _output.ToString().Should().NotContain("path not found");
    }

    [Fact]
    public void TryHandleMount_WithQuotedAbsolutePathContainingSpaces_MountsDrive() {
        // Arrange — create a subfolder whose name contains a space
        string spacyFolder = Path.Combine(_tempDir, "my games");
        Directory.CreateDirectory(spacyFolder);

        // Act — pass the path surrounded by double-quotes
        _accessor.TryHandleMount($"A \"{spacyFolder}\"");

        // Assert
        _driveManager['A'].MountedHostDirectory.Should().NotBeNullOrWhiteSpace();
        _output.ToString().Should().NotContain("path not found");
    }

    [Fact]
    public void TryHandleImgMount_WithRelativePath_MountsImage() {
        // Arrange
        byte[] imageBytes = new Fat12ImageBuilder().Build();
        string imagePath = Path.Combine(_tempDir, "rel.img");
        File.WriteAllBytes(imagePath, imageBytes);

        // Compute a relative path from the process CWD to the image file
        string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, imagePath);

        // Act
        _accessor.TryHandleImgMount($"A {relativePath} -t floppy", _mscdex);

        // Assert
        bool mounted = _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        mounted.Should().BeTrue();
        floppy!.HasImage.Should().BeTrue();
        _output.ToString().Should().NotContain("not found");
    }

    [Fact]
    public void TryHandleImgMount_WithQuotedPathContainingSpaces_MountsImage() {
        // Arrange — image file whose directory contains a space
        string spacyDir = Path.Combine(_tempDir, "my discs");
        Directory.CreateDirectory(spacyDir);
        byte[] imageBytes = new Fat12ImageBuilder().Build();
        string imagePath = Path.Combine(spacyDir, "disk.img");
        File.WriteAllBytes(imagePath, imageBytes);

        // Act — pass the path surrounded by double-quotes
        _accessor.TryHandleImgMount($"A \"{imagePath}\" -t floppy", _mscdex);

        // Assert
        bool mounted = _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        mounted.Should().BeTrue();
        floppy!.HasImage.Should().BeTrue();
    }

    [Fact]
    public void TryHandleMount_WithDosDrivePath_MountsDrive() {
        // Arrange — C: is already mounted to _tempDir; create a subdirectory inside it
        string subFolder = Path.Combine(_tempDir, "dossubdir");
        Directory.CreateDirectory(subFolder);

        // Act — pass the path using the DOS C: drive letter
        _accessor.TryHandleMount("A C:\\dossubdir");

        // Assert — A: should be mounted to the host path under _tempDir
        _driveManager['A'].MountedHostDirectory.Should().NotBeNullOrWhiteSpace();
        _output.ToString().Should().NotContain("path not found");
    }

    [Fact]
    public void TryHandleMount_RelativePathResolvesAgainstCurrentDosDirectory() {
        // Arrange — C: is already mounted to _tempDir
        // Simulate "CD games" by updating the current DOS directory on C:
        string gamesFolder = Path.Combine(_tempDir, "games");
        Directory.CreateDirectory(gamesFolder);
        string musicFolder = Path.Combine(gamesFolder, "music");
        Directory.CreateDirectory(musicFolder);
        _driveManager['C'].CurrentDosDirectory = "games";

        // Act — relative path "music" should resolve to _tempDir/games/music
        _accessor.TryHandleMount("A music");

        // Assert
        _driveManager['A'].MountedHostDirectory.Should().NotBeNullOrWhiteSpace();
        _output.ToString().Should().NotContain("path not found");
    }

    [Fact]
    public void TryHandleImgMount_WithDosDrivePath_MountsImage() {
        // Arrange — C: is already mounted to _tempDir; place a floppy image there
        byte[] imageBytes = new Fat12ImageBuilder().Build();
        string imagePath = Path.Combine(_tempDir, "dos_drive.img");
        File.WriteAllBytes(imagePath, imageBytes);

        // Act — pass the path using the DOS C: drive letter
        _accessor.TryHandleImgMount("A C:\\dos_drive.img -t floppy", _mscdex);

        // Assert
        bool mounted = _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        mounted.Should().BeTrue();
        floppy!.HasImage.Should().BeTrue();
        _output.ToString().Should().NotContain("not found");
    }

    [Fact]
    public void TryHandleImgMount_RelativePathResolvesAgainstCurrentDosDirectory() {
        // Arrange — C: mounted to _tempDir; simulate "CD discs"
        string discsFolder = Path.Combine(_tempDir, "discs");
        Directory.CreateDirectory(discsFolder);
        byte[] imageBytes = new Fat12ImageBuilder().Build();
        string imagePath = Path.Combine(discsFolder, "game.img");
        File.WriteAllBytes(imagePath, imageBytes);
        _driveManager['C'].CurrentDosDirectory = "discs";

        // Act — relative path "game.img" should resolve to _tempDir/discs/game.img
        _accessor.TryHandleImgMount("A game.img -t floppy", _mscdex);

        // Assert
        bool mounted = _driveManager.TryGetFloppyDrive('A', out FloppyDiskDrive? floppy);
        mounted.Should().BeTrue();
        floppy!.HasImage.Should().BeTrue();
    }

    // ---------- test-only helper that drives the batch command logic ----------

    [Fact]
    public void MountFolderDrive_WhenRemountingCurrentDrive_UpdatesCurrentDrive() {
        // Arrange — the drive manager starts with C: as the current drive
        string newPath = Path.Combine(_tempDir, "newC");
        Directory.CreateDirectory(newPath);
        _driveManager.CurrentDrive.DriveLetter.Should().Be('C');

        // Act
        _driveManager.MountFolderDrive('C', newPath);

        // Assert — CurrentDrive must point to the newly mounted entry
        _driveManager.CurrentDrive.MountedHostDirectory.Should().Contain("newC");
    }

    [Fact]
    public void MountFolderDrive_WhenRemountingNonCurrentDrive_DoesNotChangeCurrentDrive() {
        // Arrange
        string newPath = Path.Combine(_tempDir, "newD");
        Directory.CreateDirectory(newPath);
        char originalCurrentLetter = _driveManager.CurrentDrive.DriveLetter;

        // Act — mount D: (which is NOT the current drive)
        _driveManager.MountFolderDrive('D', newPath);

        // Assert — current drive letter is unchanged
        _driveManager.CurrentDrive.DriveLetter.Should().Be(originalCurrentLetter);
    }

    /// <summary>
    /// Test helper that exercises the MOUNT / IMGMOUNT path-resolution logic using
    /// <see cref="BatchArgumentParser"/> and <see cref="HostPathResolver"/> from production code,
    /// without requiring a full machine context.
    /// </summary>
    private sealed class DosBatchExecutionEngineAccessor {
        private readonly DosDriveManager _driveManager;
        private readonly StringBuilder _output;

        public DosBatchExecutionEngineAccessor(DosDriveManager driveManager, StringBuilder output) {
            _driveManager = driveManager;
            _output = output;
        }

        public void TryHandleMount(string arguments) {
            string trimmed = arguments.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) {
                _output.Append("Usage: MOUNT <drive> <path> [-t cdrom|floppy|hdd]\r\n");
                return;
            }

            string[] parts = BatchArgumentParser.SplitWithQuotes(trimmed);
            if (parts.Length < 2) {
                _output.Append("MOUNT: missing path argument\r\n");
                return;
            }

            char driveLetter = char.ToUpperInvariant(parts[0][0]);
            string hostPath;
            try {
                hostPath = HostPathResolver.Resolve(parts[1], _driveManager);
            } catch (ArgumentException ex) {
                _output.Append($"MOUNT: invalid path '{parts[1]}': {ex.Message}\r\n");
                return;
            } catch (NotSupportedException ex) {
                _output.Append($"MOUNT: invalid path format '{parts[1]}': {ex.Message}\r\n");
                return;
            } catch (PathTooLongException ex) {
                _output.Append($"MOUNT: path too long '{parts[1]}': {ex.Message}\r\n");
                return;
            }

            string driveType = "hdd";
            for (int i = 2; i < parts.Length - 1; i++) {
                if (parts[i].Equals("-t", StringComparison.OrdinalIgnoreCase)) {
                    driveType = parts[i + 1].ToLowerInvariant();
                    break;
                }
            }

            if (!Directory.Exists(hostPath)) {
                _output.Append($"MOUNT: path not found: {hostPath}\r\n");
                return;
            }

            if (driveType == "floppy") {
                _driveManager.MountFloppyFolder(driveLetter, hostPath);
            } else {
                _driveManager.MountFolderDrive(driveLetter, hostPath);
            }

            _output.Append($"Drive {driveLetter}: mounted as {hostPath}\r\n");
        }

        public void TryHandleImgMount(string arguments, MscdexService mscdex) {
            string trimmed = arguments.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) {
                _output.Append("Usage: IMGMOUNT <drive> <image> -t floppy|iso|cue\r\n");
                return;
            }

            string[] parts = BatchArgumentParser.SplitWithQuotes(trimmed);
            if (parts.Length < 2) {
                _output.Append("IMGMOUNT: missing image path\r\n");
                return;
            }

            char driveLetter = char.ToUpperInvariant(parts[0][0]);

            string imageType = string.Empty;
            List<string> imagePaths = new();
            for (int i = 1; i < parts.Length; i++) {
                if (parts[i].Equals("-t", StringComparison.OrdinalIgnoreCase)) {
                    if (i + 1 < parts.Length) {
                        imageType = parts[i + 1].ToLowerInvariant();
                    }
                    break;
                }
                string resolved;
                try {
                    resolved = HostPathResolver.Resolve(parts[i], _driveManager);
                } catch (ArgumentException ex) {
                    _output.Append($"IMGMOUNT: invalid path '{parts[i]}': {ex.Message}\r\n");
                    return;
                } catch (NotSupportedException ex) {
                    _output.Append($"IMGMOUNT: invalid path format '{parts[i]}': {ex.Message}\r\n");
                    return;
                } catch (PathTooLongException ex) {
                    _output.Append($"IMGMOUNT: path too long '{parts[i]}': {ex.Message}\r\n");
                    return;
                }
                imagePaths.Add(resolved);
            }

            if (imagePaths.Count == 0) {
                _output.Append("IMGMOUNT: missing image path\r\n");
                return;
            }

            string firstPath = imagePaths[0];

            if (string.IsNullOrEmpty(imageType)) {
                string ext = Path.GetExtension(firstPath).ToLowerInvariant();
                if (ext == ".img" || ext == ".ima" || ext == ".vfd") {
                    imageType = "floppy";
                } else if (ext == ".iso") {
                    imageType = "iso";
                } else if (ext == ".cue") {
                    imageType = "cue";
                } else {
                    _output.Append($"IMGMOUNT: cannot detect image type for '{firstPath}'. Use -t floppy|iso|cue.\r\n");
                    return;
                }
            }

            if (!File.Exists(firstPath)) {
                _output.Append($"IMGMOUNT: image file not found: {firstPath}\r\n");
                return;
            }

            if (imageType == "floppy") {
                byte[] imageData = File.ReadAllBytes(firstPath);
                _driveManager.MountFloppyImage(driveLetter, imageData, firstPath);
                _output.Append($"Drive {driveLetter}: mounted as floppy image {firstPath}\r\n");
            } else {
                _output.Append($"IMGMOUNT: unsupported image type '{imageType}'\r\n");
            }
        }
    }
}
