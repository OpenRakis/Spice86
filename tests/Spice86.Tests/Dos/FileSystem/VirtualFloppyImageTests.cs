namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.OperatingSystem.FileSystem;
using Spice86.Shared.Interfaces;

using System;
using System.IO;
using System.Text;

using Xunit;

/// <summary>
/// Tests for <see cref="VirtualFloppyImage"/> — builds a FAT12 floppy image from a host directory.
/// </summary>
public sealed class VirtualFloppyImageTests : IDisposable {
    private readonly string _testDir;

    public VirtualFloppyImageTests() {
        _testDir = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose() {
        if (Directory.Exists(_testDir)) {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private static ILoggerService CreateLogger() => Substitute.For<ILoggerService>();

    [Fact]
    public void Build_EmptyDirectory_ReturnsCorrectImageSize() {
        // Arrange
        VirtualFloppyImage builder = new(_testDir, CreateLogger());

        // Act
        byte[] image = builder.Build();

        // Assert
        image.Should().HaveCount(2880 * 512, "1.44 MB floppy image is 2880 sectors × 512 bytes");
    }

    [Fact]
    public void Build_WithSingleFile_FilePresentInFatFilesystem() {
        // Arrange
        byte[] content = Encoding.ASCII.GetBytes("HELLO FAT12");
        File.WriteAllBytes(Path.Combine(_testDir, "TEST.TXT"), content);
        VirtualFloppyImage builder = new(_testDir, CreateLogger());

        // Act
        byte[] image = builder.Build();
        FatFileSystem fs = new(image);

        // Assert
        fs.FatType.Should().Be(FatType.Fat12);
        bool found = fs.TryGetEntry("TEST.TXT", out FatDirectoryEntry? entry);
        found.Should().BeTrue();
        entry.Should().NotBeNull();
    }

    [Fact]
    public void Build_WithSingleFile_FileContentIsCorrect() {
        // Arrange
        byte[] expected = Encoding.ASCII.GetBytes("HELLO FAT12 WORLD");
        File.WriteAllBytes(Path.Combine(_testDir, "HELLO.TXT"), expected);
        VirtualFloppyImage builder = new(_testDir, CreateLogger());

        // Act
        byte[] image = builder.Build();
        FatFileSystem fs = new(image);
        fs.TryGetEntry("HELLO.TXT", out FatDirectoryEntry? entry);
        byte[] actual = fs.ReadFile(entry!);

        // Assert
        actual.Should().StartWith(expected);
    }

    [Fact]
    public void Build_WithSubdirectory_SubdirAndFileAreAccessible() {
        // Arrange
        string subDir = Path.Combine(_testDir, "SUBDIR");
        Directory.CreateDirectory(subDir);
        byte[] content = Encoding.ASCII.GetBytes("IN SUBDIR");
        File.WriteAllBytes(Path.Combine(subDir, "FILE.TXT"), content);
        VirtualFloppyImage builder = new(_testDir, CreateLogger());

        // Act
        byte[] image = builder.Build();
        FatFileSystem fs = new(image);
        bool found = fs.TryGetEntry("SUBDIR\\FILE.TXT", out FatDirectoryEntry? entry);

        // Assert
        found.Should().BeTrue();
        entry.Should().NotBeNull();
    }

    [Fact]
    public void Build_OversizeFile_LogsWarningAndSkipsFile() {
        // Arrange
        ILoggerService logger = Substitute.For<ILoggerService>();
        logger.IsEnabled(Serilog.Events.LogEventLevel.Warning).Returns(true);

        // Create a file that won't fit (bigger than remaining data area: 2847 clusters × 512 bytes = ~1.4 MB)
        byte[] large = new byte[2880 * 512]; // full disk size in one file — won't fit
        File.WriteAllBytes(Path.Combine(_testDir, "BIG.BIN"), large);
        VirtualFloppyImage builder = new(_testDir, logger);

        // Act
        byte[] image = builder.Build();
        FatFileSystem fs = new(image);
        bool found = fs.TryGetEntry("BIG.BIN", out FatDirectoryEntry? _);

        // Assert
        found.Should().BeFalse("file too large to fit should be skipped");
        logger.Received().IsEnabled(Serilog.Events.LogEventLevel.Warning);
    }
}
