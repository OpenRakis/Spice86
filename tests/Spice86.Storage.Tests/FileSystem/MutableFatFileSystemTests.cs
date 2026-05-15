namespace Spice86.Storage.Tests.FileSystem;

using FluentAssertions;
using Spice86.Shared.Emulator.Storage.FileSystem;
using Xunit;

/// <summary>
/// TDD tests for Phase 1e: MutableFatFileSystem integration.
/// Tests verify full read+write FAT filesystem operations including file creation,
/// deletion, renaming, truncation, boot sector modification, and dirty tracking.
/// </summary>
public class MutableFatFileSystemTests {
    /// <summary>
    /// Helper: Create minimal FAT12 image (512-byte boot sector + FAT + root dir).
    /// </summary>
    private static byte[] CreateMinimalFat12Image() {
        // Create a simple 512KB FAT12 image
        byte[] image = new byte[512 * 1024];
        
        // Boot sector (512 bytes)
        byte[] bootSector = new byte[512];
        bootSector[0x00] = 0xEB; // JMP instruction
        bootSector[0x01] = 0x3C;
        bootSector[0x02] = 0x90;
        // OEM ID at offset 0x03
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("SPICE86 "), 0, bootSector, 0x03, 8);
        // Bytes per sector = 512
        bootSector[0x0B] = 0x00;
        bootSector[0x0C] = 0x02;
        // Sectors per cluster = 8
        bootSector[0x0D] = 0x08;
        // Reserved sectors = 1
        bootSector[0x0E] = 0x01;
        bootSector[0x0F] = 0x00;
        // Number of FATs = 2
        bootSector[0x10] = 0x02;
        // Max root dir entries = 224
        bootSector[0x11] = 0xE0;
        bootSector[0x12] = 0x00;
        // Total sectors = 1024 (512 * 1024 bytes / 512)
        bootSector[0x13] = 0x00;
        bootSector[0x14] = 0x04;
        // Media type = 0xF8 (hard disk)
        bootSector[0x15] = 0xF8;
        // Sectors per FAT = 4
        bootSector[0x16] = 0x04;
        bootSector[0x17] = 0x00;
        // Boot signature
        bootSector[0x1FE] = 0x55;
        bootSector[0x1FF] = 0xAA;
        
        Buffer.BlockCopy(bootSector, 0, image, 0, 512);
        return image;
    }

    /// <summary>
    /// Test: CreateFile on FAT12 returns correctly and round-trips when serialized.
    /// </summary>
    [Fact]
    public void CreateFile_FAT12_RoundTrips() {
        // Arrange
        byte[] diskImage = CreateMinimalFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        
        // Act
        fs.CreateFile("FILE.TXT", new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }); // "Hello"
        fs.CommitChanges(diskImage);

        // Assert
        fs.IsDirty.Should().BeFalse();
        MutableFatFileSystem fsReloaded = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] content = fsReloaded.ReadFile("FILE.TXT");
        content.Should().Equal(0x48, 0x65, 0x6C, 0x6C, 0x6F);
    }

    /// <summary>
    /// Test: CreateFile with large file spanning multiple clusters.
    /// </summary>
    [Fact]
    public void CreateFile_LargeFile_SpansClusters() {
        // Arrange
        byte[] diskImage = CreateMinimalFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        
        // Act: Create 5KB file (spans multiple 4KB clusters)
        byte[] largeContent = new byte[5120];
        for (int i = 0; i < largeContent.Length; i++) {
            largeContent[i] = (byte)(i % 256);
        }
        fs.CreateFile("LARGE.BIN", largeContent);
        fs.CommitChanges(diskImage);

        // Assert
        fs.IsDirty.Should().BeFalse();
        MutableFatFileSystem fsReloaded = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] readback = fsReloaded.ReadFile("LARGE.BIN");
        readback.Should().Equal(largeContent);
    }

    /// <summary>
    /// Test: DeleteFile frees cluster chain in FAT.
    /// </summary>
    [Fact]
    public void DeleteFile_FreesClusterChain() {
        // Arrange
        byte[] diskImage = CreateMinimalFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        
        // Act
        byte[] content = new byte[4096]; // 1 cluster
        fs.CreateFile("FILE.TXT", content);
        uint freeClustersBefore = fs.FreeClusterCount;
        fs.DeleteFile("FILE.TXT");
        uint freeClustersAfter = fs.FreeClusterCount;

        // Assert
        freeClustersAfter.Should().BeGreaterThan(freeClustersBefore);
    }

    /// <summary>
    /// Test: RenameEntry updates directory entry.
    /// </summary>
    [Fact]
    public void RenameEntry_UpdatesDirEntry() {
        // Arrange
        byte[] diskImage = CreateMinimalFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        
        // Act
        fs.CreateFile("OLD.TXT", new byte[] { 0x44, 0x41, 0x54, 0x41 }); // "DATA"
        fs.RenameEntry("OLD.TXT", "NEW.TXT");
        fs.CommitChanges(diskImage);

        // Assert
        fs.IsDirty.Should().BeFalse();
        MutableFatFileSystem fsReloaded = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte[] content = fsReloaded.ReadFile("NEW.TXT");
        content.Should().Equal(0x44, 0x41, 0x54, 0x41);
        fsReloaded.Invoking(f => f.ReadFile("OLD.TXT")).Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// Test: TruncateFile shrinks file and frees excess clusters.
    /// </summary>
    [Fact]
    public void TruncateFile_ShrinksThenFreesClusters() {
        // Arrange
        byte[] diskImage = CreateMinimalFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        
        // Act
        byte[] originalContent = new byte[8192]; // 2 clusters
        fs.CreateFile("FILE.BIN", originalContent);
        uint freeClustersBefore = fs.FreeClusterCount;
        fs.TruncateFile("FILE.BIN", 1024); // Truncate to 1KB
        uint freeClustersAfter = fs.FreeClusterCount;

        // Assert
        freeClustersAfter.Should().BeGreaterThan(freeClustersBefore);
        byte[] truncated = fs.ReadFile("FILE.BIN");
        truncated.Length.Should().Be(1024);
    }

    /// <summary>
    /// Test: WriteBootSector mutates BPB in BootSector property.
    /// </summary>
    [Fact]
    public void WriteBootSector_BpbMutated() {
        // Arrange
        byte[] diskImage = CreateMinimalFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        byte originalVolId = fs.BootSector.MediaType;

        // Act
        fs.WriteBootSector(bpb => {
            bpb.MediaType = 0xF0;
        });

        // Assert
        fs.BootSector.MediaType.Should().Be(0xF0);
        fs.BootSector.MediaType.Should().NotBe(originalVolId);
    }

    /// <summary>
    /// Test: FatFileSystemWriter serializes all FAT copies identically.
    /// </summary>
    [Fact]
    public void FatFileSystemWriter_Serialize_AllFatCopiesIdentical() {
        // Arrange
        byte[] diskImage = CreateMinimalFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        fs.CreateFile("TEST.TXT", new byte[] { 0x41, 0x42, 0x43 });

        // Act
        FatFileSystemWriter writer = new FatFileSystemWriter();
        writer.Serialize(fs, diskImage);

        // Assert: Verify FAT copies are identical (FAT is at sector 1 and next copy)
        // FAT size = 4 sectors (from boot sector), cluster size = 8 sectors = 4096 bytes
        int fatStartSector = 1; // After boot sector
        int sectorsPerFat = 4;
        
        byte[] fat1 = new byte[512 * sectorsPerFat];
        byte[] fat2 = new byte[512 * sectorsPerFat];
        Array.Copy(diskImage, fatStartSector * 512, fat1, 0, 512 * sectorsPerFat);
        Array.Copy(diskImage, (fatStartSector + sectorsPerFat) * 512, fat2, 0, 512 * sectorsPerFat);
        fat1.Should().Equal(fat2);
    }

    /// <summary>
    /// Test: CommitChanges FAT12 round-trips correctly.
    /// </summary>
    [Fact]
    public void CommitChanges_FAT12_RoundTrip() {
        // Arrange
        byte[] diskImage = CreateMinimalFat12Image();
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

    /// <summary>
    /// Test: DirtyFlag tracks uncommitted changes.
    /// </summary>
    [Fact]
    public void DirtyFlag_TracksChanges() {
        // Arrange
        byte[] diskImage = CreateMinimalFat12Image();
        MutableFatFileSystem fs = new MutableFatFileSystem(diskImage, FatType.Fat12);
        
        // Act & Assert
        fs.IsDirty.Should().BeFalse(); // Initially clean
        fs.CreateFile("NEW.TXT", new byte[] { 0x41 });
        fs.IsDirty.Should().BeTrue(); // Dirty after create
        fs.CommitChanges(diskImage);
        fs.IsDirty.Should().BeFalse(); // Clean after commit
    }
}

