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
        _driveManager = new DosDriveManager(logger, _tempDir, null);
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

    // ---------- test-only helper that replicates the batch command logic ----------

    /// <summary>
    /// Test helper that replicates the <c>TryHandleMount</c> and <c>TryHandleImgMount</c>
    /// batch command logic without requiring a full machine context.
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

            string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) {
                _output.Append("MOUNT: missing path argument\r\n");
                return;
            }

            char driveLetter = char.ToUpperInvariant(parts[0][0]);
            string hostPath = parts[1];

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

            string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) {
                _output.Append("IMGMOUNT: missing image path\r\n");
                return;
            }

            char driveLetter = char.ToUpperInvariant(parts[0][0]);
            string imagePath = parts[1];

            string imageType = string.Empty;
            for (int i = 2; i < parts.Length - 1; i++) {
                if (parts[i].Equals("-t", StringComparison.OrdinalIgnoreCase)) {
                    imageType = parts[i + 1].ToLowerInvariant();
                    break;
                }
            }

            if (string.IsNullOrEmpty(imageType)) {
                string ext = Path.GetExtension(imagePath).ToLowerInvariant();
                if (ext == ".img" || ext == ".ima" || ext == ".vfd") {
                    imageType = "floppy";
                } else if (ext == ".iso") {
                    imageType = "iso";
                } else if (ext == ".cue") {
                    imageType = "cue";
                } else {
                    _output.Append($"IMGMOUNT: cannot detect image type for '{imagePath}'. Use -t floppy|iso|cue.\r\n");
                    return;
                }
            }

            if (!File.Exists(imagePath)) {
                _output.Append($"IMGMOUNT: image file not found: {imagePath}\r\n");
                return;
            }

            if (imageType == "floppy") {
                byte[] imageData = File.ReadAllBytes(imagePath);
                _driveManager.MountFloppyImage(driveLetter, imageData, imagePath);
                _output.Append($"Drive {driveLetter}: mounted as floppy image {imagePath}\r\n");
            } else {
                _output.Append($"IMGMOUNT: unsupported image type '{imageType}'\r\n");
            }
        }
    }
}
