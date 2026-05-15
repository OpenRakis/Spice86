namespace Spice86.Storage.Tests.FileSystem;

using System;
using System.IO;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.FileSystem;

using Xunit;

/// <summary>
/// TDD tests for Phase 6: file-backed FAT image with write-back.
/// Covers loading a real .IMG file from disk, mutating it through
/// <see cref="MutableFatFileSystem"/>, and persisting the changes via
/// <see cref="FileBackedFatImage.Flush"/>.
/// </summary>
public class FileBackedFatImageTests : IDisposable
{
    private readonly string _tempDir;

    public FileBackedFatImageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Spice86_FileBackedFatImageTests_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_tempDir))
        {
            System.IO.Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Open_ExistingImage_ParsesFat12FileSystem()
    {
        // Arrange
        string imagePath = WriteEmptyFat12ImageToDisk("empty.img");

        // Act
        using FileBackedFatImage image = FileBackedFatImage.Open(imagePath, FatType.Fat12);

        // Assert
        image.FileSystem.FatType.Should().Be(FatType.Fat12);
        image.FileSystem.IsDirty.Should().BeFalse();
        image.Path.Should().Be(imagePath);
    }

    [Fact]
    public void Flush_AfterCreateFile_PersistsBytesToDisk()
    {
        // Arrange
        string imagePath = WriteEmptyFat12ImageToDisk("written.img");
        byte[] content = new byte[] { 0x48, 0x69, 0x21 };
        using (FileBackedFatImage image = FileBackedFatImage.Open(imagePath, FatType.Fat12))
        {
            image.FileSystem.CreateFile("HELLO.TXT", content);

            // Act
            image.Flush();

            // Assert
            image.FileSystem.IsDirty.Should().BeFalse();
        }

        using FileBackedFatImage reopened = FileBackedFatImage.Open(imagePath, FatType.Fat12);
        byte[] roundTripped = reopened.FileSystem.ReadFile("HELLO.TXT");
        roundTripped.Should().Equal(content);
    }

    [Fact]
    public void Dispose_WithDirtyFileSystem_FlushesAutomatically()
    {
        // Arrange
        string imagePath = WriteEmptyFat12ImageToDisk("autoflush.img");
        byte[] content = new byte[] { 0xAA, 0xBB, 0xCC };

        // Act
        using (FileBackedFatImage image = FileBackedFatImage.Open(imagePath, FatType.Fat12))
        {
            image.FileSystem.CreateFile("AUTO.BIN", content);
            // No explicit Flush — Dispose must persist.
        }

        // Assert
        using FileBackedFatImage reopened = FileBackedFatImage.Open(imagePath, FatType.Fat12);
        reopened.FileSystem.ReadFile("AUTO.BIN").Should().Equal(content);
    }

    [Fact]
    public void Open_NonExistentImage_ThrowsFileNotFoundException()
    {
        // Arrange
        string missing = Path.Combine(_tempDir, "does-not-exist.img");

        // Act
        Action act = () => FileBackedFatImage.Open(missing, FatType.Fat12);

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    private string WriteEmptyFat12ImageToDisk(string fileName)
    {
        string path = Path.Combine(_tempDir, fileName);
        byte[] image = new Fat12ImageBuilder().Build();
        File.WriteAllBytes(path, image);
        return path;
    }
}
