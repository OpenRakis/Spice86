namespace Spice86.Storage.Tests.FileSystem;

using FluentAssertions;

using NSubstitute;

using Serilog;
using Serilog.Events;

using Spice86.Shared.Emulator.Storage.FileSystem;

using System;
using System.IO;
using System.Text;

using Xunit;

/// <summary>
/// Tests for <see cref="VirtualFloppyImage"/> - builds a FAT12 floppy image from a host directory.
/// </summary>
public sealed class VirtualFloppyImageTests : IDisposable {
    private readonly string _testDir;

    public VirtualFloppyImageTests() {
        _testDir = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(_testDir);
    }

    public void Dispose() {
        if (System.IO.Directory.Exists(_testDir)) {
            System.IO.Directory.Delete(_testDir, recursive: true);
        }
    }

    private static ILogger CreateLogger() => Substitute.For<ILogger>();

    [Fact]
    public void Build_EmptyDirectory_ReturnsCorrectImageSize() {
        // Arrange
        VirtualFloppyImage builder = new(_testDir, CreateLogger());

        // Act
        byte[] image = builder.Build();

        // Assert
        image.Should().HaveCount(2880 * 512, "1.44 MB floppy image is 2880 sectors x 512 bytes");
    }

    [Fact]
    public void Build_NullLogger_DoesNotThrow() {
        // Arrange
        VirtualFloppyImage builder = new(_testDir, logger: null);

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Build_WithSingleFile_FilePresentInFatFilesystem() {
        // Arrange
        byte[] content = Encoding.ASCII.GetBytes("HELLO FAT12");
        System.IO.File.WriteAllBytes(Path.Combine(_testDir, "TEST.TXT"), content);
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
        System.IO.File.WriteAllBytes(Path.Combine(_testDir, "HELLO.TXT"), expected);
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
        System.IO.Directory.CreateDirectory(subDir);
        byte[] content = Encoding.ASCII.GetBytes("IN SUBDIR");
        System.IO.File.WriteAllBytes(Path.Combine(subDir, "FILE.TXT"), content);
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
    public void Build_WithNestedSubdirectories_NestedFileIsAccessible() {
        // Arrange
        string level1 = Path.Combine(_testDir, "LEVEL1");
        string level2 = Path.Combine(level1, "LEVEL2");
        System.IO.Directory.CreateDirectory(level2);
        byte[] content = Encoding.ASCII.GetBytes("DEEP FILE");
        System.IO.File.WriteAllBytes(Path.Combine(level2, "DEEP.TXT"), content);
        VirtualFloppyImage builder = new(_testDir, CreateLogger());

        // Act
        byte[] image = builder.Build();
        FatFileSystem fs = new(image);
        bool found = fs.TryGetEntry("LEVEL1\\LEVEL2\\DEEP.TXT", out FatDirectoryEntry? entry);

        // Assert
        found.Should().BeTrue();
        entry.Should().NotBeNull();
    }

    [Fact]
    public void Build_WithNestedSubdirectory_NestedDirectoryIsAccessible() {
        // Arrange
        string level1 = Path.Combine(_testDir, "LEVEL1");
        string level2 = Path.Combine(level1, "LEVEL2");
        System.IO.Directory.CreateDirectory(level2);
        VirtualFloppyImage builder = new(_testDir, CreateLogger());

        // Act
        byte[] image = builder.Build();
        FatFileSystem fs = new(image);
        bool found = fs.TryGetEntry("LEVEL1\\LEVEL2", out FatDirectoryEntry? entry);

        // Assert
        found.Should().BeTrue();
        entry.Should().NotBeNull();
        entry.IsDirectory.Should().BeTrue();
    }

    [Fact]
    public void Build_OversizeFile_LogsWarningAndSkipsFile() {
        // Arrange
        ILogger logger = Substitute.For<ILogger>();
        logger.IsEnabled(LogEventLevel.Warning).Returns(true);

        // Create a file that won't fit (bigger than remaining data area).
        byte[] large = new byte[2880 * 512];
        System.IO.File.WriteAllBytes(Path.Combine(_testDir, "BIG.BIN"), large);
        VirtualFloppyImage builder = new(_testDir, logger);

        // Act
        byte[] image = builder.Build();
        FatFileSystem fs = new(image);
        bool found = fs.TryGetEntry("BIG.BIN", out FatDirectoryEntry? _);

        // Assert
        found.Should().BeFalse("file too large to fit should be skipped");
        logger.Received().IsEnabled(LogEventLevel.Warning);
    }
}
