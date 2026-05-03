namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.FileSystem;

using System.Text;

using Xunit;

/// <summary>
/// Unit tests for <see cref="Fat12FileSystem"/> reading files from a floppy image.
/// </summary>
public class Fat12FileSystemTests {
    [Fact]
    public void VolumeLabel_ReturnsBpbLabel() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();

        // Act
        Fat12FileSystem fs = new Fat12FileSystem(image);

        // Assert
        fs.VolumeLabel.Should().Be("TEST FLOPPY");
    }

    [Fact]
    public void ListRootDirectory_WithOneFile_ReturnsThatFile() {
        // Arrange
        byte[] content = Encoding.ASCII.GetBytes("Hello world");
        byte[] image = new Fat12ImageBuilder()
            .WithFile("README.TXT", content)
            .Build();

        // Act
        Fat12FileSystem fs = new Fat12FileSystem(image);
        System.Collections.Generic.IReadOnlyList<FatDirectoryEntry> entries = fs.ListRootDirectory();

        // Assert
        entries.Should().ContainSingle(e => e.DosName == "README.TXT");
    }

    [Fact]
    public void ReadFile_SmallFile_ReturnsCorrectContent() {
        // Arrange
        byte[] expected = Encoding.ASCII.GetBytes("Hello world");
        byte[] image = new Fat12ImageBuilder()
            .WithFile("README.TXT", expected)
            .Build();
        Fat12FileSystem fs = new Fat12FileSystem(image);
        FatDirectoryEntry entry = fs.ListRootDirectory()[0];

        // Act
        byte[] actual = fs.ReadFile(entry);

        // Assert
        actual.Should().Equal(expected);
    }

    [Fact]
    public void TryGetEntry_ExistingFile_ReturnsTrue() {
        // Arrange
        byte[] content = Encoding.ASCII.GetBytes("data");
        byte[] image = new Fat12ImageBuilder()
            .WithFile("AUTOEXEC.BAT", content)
            .Build();
        Fat12FileSystem fs = new Fat12FileSystem(image);

        // Act
        bool found = fs.TryGetEntry("AUTOEXEC.BAT", out FatDirectoryEntry? entry);

        // Assert
        found.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.DosName.Should().Be("AUTOEXEC.BAT");
    }

    [Fact]
    public void TryGetEntry_MissingFile_ReturnsFalse() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        Fat12FileSystem fs = new Fat12FileSystem(image);

        // Act
        bool found = fs.TryGetEntry("MISSING.TXT", out _);

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public void Exists_ExistingFile_ReturnsTrue() {
        // Arrange
        byte[] content = Encoding.ASCII.GetBytes("hello");
        byte[] image = new Fat12ImageBuilder()
            .WithFile("CONFIG.SYS", content)
            .Build();
        Fat12FileSystem fs = new Fat12FileSystem(image);

        // Act & Assert
        fs.Exists("CONFIG.SYS").Should().BeTrue();
    }

    [Fact]
    public void Exists_MissingFile_ReturnsFalse() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();
        Fat12FileSystem fs = new Fat12FileSystem(image);

        // Act & Assert
        fs.Exists("NOTHERE.TXT").Should().BeFalse();
    }

    [Fact]
    public void ReadFile_MultipleFilesInRoot_ReadsEachCorrectly() {
        // Arrange
        byte[] file1Content = Encoding.ASCII.GetBytes("File one content");
        byte[] file2Content = Encoding.ASCII.GetBytes("File two content here");
        byte[] image = new Fat12ImageBuilder()
            .WithFile("FILE1.TXT", file1Content)
            .WithFile("FILE2.TXT", file2Content)
            .Build();
        Fat12FileSystem fs = new Fat12FileSystem(image);

        // Act
        System.Collections.Generic.IReadOnlyList<FatDirectoryEntry> entries = fs.ListRootDirectory();
        byte[] actual1 = fs.ReadFile(entries[0]);
        byte[] actual2 = fs.ReadFile(entries[1]);

        // Assert
        actual1.Should().Equal(file1Content);
        actual2.Should().Equal(file2Content);
    }

    [Fact]
    public void ReadFile_OnDirectory_ThrowsInvalidOperationException() {
        // Arrange
        byte[] content = Encoding.ASCII.GetBytes("hello");
        byte[] image = new Fat12ImageBuilder()
            .WithSubdirectoryAndFile("SUBDIR", "FILE.TXT", content)
            .Build();
        Fat12FileSystem fs = new Fat12FileSystem(image);
        FatDirectoryEntry dirEntry = fs.ListRootDirectory()[0];

        // Act
        System.Action readDir = () => fs.ReadFile(dirEntry);

        // Assert
        readDir.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryGetEntry_CaseInsensitive_FindsFile() {
        // Arrange
        byte[] content = Encoding.ASCII.GetBytes("data");
        byte[] image = new Fat12ImageBuilder()
            .WithFile("AUTOEXEC.BAT", content)
            .Build();
        Fat12FileSystem fs = new Fat12FileSystem(image);

        // Act
        bool found = fs.TryGetEntry("autoexec.bat", out _);

        // Assert
        found.Should().BeTrue();
    }
}
