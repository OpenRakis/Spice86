namespace Spice86.Storage.Tests.FileSystem;

using System;
using System.IO;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.FileSystem;

using Xunit;

/// <summary>
/// TDD tests for Phase 1e: MutableFatFileSystem integration.
/// Tests verify full read+write FAT filesystem operations using a real
/// <see cref="Fat12ImageBuilder"/> for image construction (no hand-rolled byte arrays).
/// </summary>
public class MutableFatFileSystemTests
{
    private static byte[] EmptyFat12Image() => new Fat12ImageBuilder().Build();

    [Fact]
    public void CreateFile_FAT12_RoundTrips()
    {
        // Arrange
        byte[] diskImage = EmptyFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] content = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        // Act
        fs.CreateFile("FILE.TXT", content);
        fs.CommitChanges(diskImage);

        // Assert
        fs.IsDirty.Should().BeFalse();
        MutableFatFileSystem fsReloaded = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] readback = fsReloaded.ReadFile("FILE.TXT");
        readback.Should().Equal(content);
    }

    [Fact]
    public void CreateFile_LargeFile_SpansClusters()
    {
        // Arrange
        byte[] diskImage = EmptyFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] largeContent = new byte[5120];
        for (int i = 0; i < largeContent.Length; i++)
        {
            largeContent[i] = (byte)(i % 256);
        }

        // Act
        fs.CreateFile("LARGE.BIN", largeContent);
        fs.CommitChanges(diskImage);

        // Assert
        fs.IsDirty.Should().BeFalse();
        MutableFatFileSystem fsReloaded = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] readback = fsReloaded.ReadFile("LARGE.BIN");
        readback.Should().Equal(largeContent);
    }

    [Fact]
    public void DeleteFile_FreesClusterChain()
    {
        // Arrange
        byte[] diskImage = new Fat12ImageBuilder().WithFile("KEEP.TXT", new byte[] { 0x01 }).Build();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] content = new byte[2048];
        fs.CreateFile("FILE.TXT", content);
        uint freeBefore = fs.FreeClusterCount;

        // Act
        fs.DeleteFile("FILE.TXT");
        uint freeAfter = fs.FreeClusterCount;

        // Assert
        freeAfter.Should().BeGreaterThan(freeBefore);
    }

    [Fact]
    public void RenameEntry_UpdatesDirEntry()
    {
        // Arrange
        byte[] diskImage = EmptyFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] content = new byte[] { 0x44, 0x41, 0x54, 0x41 };

        // Act
        fs.CreateFile("OLD.TXT", content);
        fs.RenameEntry("OLD.TXT", "NEW.TXT");
        fs.CommitChanges(diskImage);

        // Assert
        fs.IsDirty.Should().BeFalse();
        MutableFatFileSystem fsReloaded = new MutableFatFileSystem(diskImage, FatType.Fat12);
        fsReloaded.ReadFile("NEW.TXT").Should().Equal(content);
        fsReloaded.Invoking(f => f.ReadFile("OLD.TXT")).Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void TruncateFile_ShrinksThenFreesClusters()
    {
        // Arrange
        byte[] diskImage = EmptyFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] originalContent = new byte[8192];
        fs.CreateFile("FILE.BIN", originalContent);
        uint freeBefore = fs.FreeClusterCount;

        // Act
        fs.TruncateFile("FILE.BIN", 1024);
        uint freeAfter = fs.FreeClusterCount;

        // Assert
        freeAfter.Should().BeGreaterThan(freeBefore);
        byte[] truncated = fs.ReadFile("FILE.BIN");
        truncated.Length.Should().Be(1024);
    }

    [Fact]
    public void WriteBootSector_BpbMutated()
    {
        // Arrange
        byte[] diskImage = EmptyFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte originalMedia = fs.BootSector.MediaDescriptor;

        // Act
        fs.WriteBootSector(bpb => bpb.MediaDescriptor = 0xF8);

        // Assert
        fs.BootSector.MediaDescriptor.Should().Be(0xF8);
        fs.BootSector.MediaDescriptor.Should().NotBe(originalMedia);
        fs.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void FatFileSystemWriter_Serialize_AllFatCopiesIdentical()
    {
        // Arrange
        byte[] diskImage = EmptyFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        fs.CreateFile("TEST.TXT", new byte[] { 0x41, 0x42, 0x43 });

        // Act
        new FatFileSystemWriter().Serialize(fs, diskImage);

        // Assert: FAT1 and FAT2 must be byte-identical
        const int bytesPerSector = 512;
        const int fatStartSector = 1;
        int sectorsPerFat = fs.BootSector.SectorsPerFat;
        byte[] fat1 = new byte[bytesPerSector * sectorsPerFat];
        byte[] fat2 = new byte[bytesPerSector * sectorsPerFat];
        Array.Copy(diskImage, fatStartSector * bytesPerSector, fat1, 0, fat1.Length);
        Array.Copy(diskImage, (fatStartSector + sectorsPerFat) * bytesPerSector, fat2, 0, fat2.Length);
        fat1.Should().Equal(fat2);
    }

    [Fact]
    public void CommitChanges_FAT12_RoundTrip()
    {
        // Arrange
        byte[] diskImage = EmptyFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);

        // Act
        fs.CreateFile("A.TXT", new byte[] { 0x41 });
        fs.CreateFile("B.TXT", new byte[] { 0x42 });
        fs.CommitChanges(diskImage);

        // Assert
        MutableFatFileSystem fsReloaded = new MutableFatFileSystem(diskImage, FatType.Fat12);
        fsReloaded.ReadFile("A.TXT").Should().Equal(0x41);
        fsReloaded.ReadFile("B.TXT").Should().Equal(0x42);
    }

    [Fact]
    public void DirtyFlag_TracksChanges()
    {
        // Arrange
        byte[] diskImage = EmptyFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);

        // Act & Assert
        fs.IsDirty.Should().BeFalse();
        fs.CreateFile("NEW.TXT", new byte[] { 0x41 });
        fs.IsDirty.Should().BeTrue();
        fs.CommitChanges(diskImage);
        fs.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void ReadFile_FileNotFound_ThrowsException()
    {
        // Arrange
        byte[] diskImage = EmptyFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);

        // Act
        Action act = () => fs.ReadFile("MISSING.TXT");

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }
}
