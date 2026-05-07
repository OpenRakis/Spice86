namespace Spice86.Tests.Dos.FileSystem;

using FluentAssertions;

using Spice86.Core.Emulator.OperatingSystem.FileSystem;

using System.Text;

using Xunit;

/// <summary>
/// Unit tests for <see cref="FatFileSystem"/> covering FAT12, FAT16, and FAT32 volumes.
/// </summary>
public class FatFileSystemTests {
    [Fact]
    public void FatType_Fat12Image_ReturnsFat12() {
        // Arrange
        byte[] image = new Fat12ImageBuilder().Build();

        // Act
        FatFileSystem fs = new FatFileSystem(image);

        // Assert
        fs.FatType.Should().Be(FatType.Fat12);
    }

    [Fact]
    public void FatType_Fat16Image_ReturnsFat16() {
        // Arrange
        byte[] image = CreateFat16Image(null);

        // Act
        FatFileSystem fs = new FatFileSystem(image);

        // Assert
        fs.FatType.Should().Be(FatType.Fat16);
    }

    [Fact]
    public void FatType_Fat32Image_ReturnsFat32() {
        // Arrange
        byte[] image = CreateMinimalFat32Image();

        // Act
        FatFileSystem fs = new FatFileSystem(image);

        // Assert
        fs.FatType.Should().Be(FatType.Fat32);
    }

    [Fact]
    public void ListRootDirectory_Fat16_FindsFile() {
        // Arrange
        byte[] content = Encoding.ASCII.GetBytes("hello fat16");
        byte[] image = CreateFat16Image(content);

        // Act
        FatFileSystem fs = new FatFileSystem(image);
        System.Collections.Generic.IReadOnlyList<FatDirectoryEntry> entries = fs.ListRootDirectory();

        // Assert
        entries.Should().ContainSingle(e => e.DosName == "TEST.TXT");
    }

    [Fact]
    public void ReadFile_Fat16_ReturnsCorrectContent() {
        // Arrange
        byte[] expected = Encoding.ASCII.GetBytes("hello fat16");
        byte[] image = CreateFat16Image(expected);
        FatFileSystem fs = new FatFileSystem(image);
        FatDirectoryEntry entry = fs.ListRootDirectory()[0];

        // Act
        byte[] actual = fs.ReadFile(entry);

        // Assert
        actual.Should().Equal(expected);
    }

    private static byte[] CreateFat16Image(byte[]? fileContent) {
        const ushort bytesPerSector = 512;
        const byte sectorsPerCluster = 1;
        const ushort reservedSectors = 2;
        const byte numberOfFats = 2;
        const ushort rootDirEntries = 16;
        const ushort sectorsPerFat = 8;
        // TotalSectors16 must yield > 4085 clusters for FAT16 detection.
        // dataStartSector = 2 + 2*8 + 1 = 19; clusters = (4200 - 19) / 1 = 4181 > 4085
        const ushort totalSectors16 = 4200;

        int rootDirSectors = (rootDirEntries * 32 + bytesPerSector - 1) / bytesPerSector;
        int dataStartSector = reservedSectors + numberOfFats * sectorsPerFat + rootDirSectors;

        // Physical image only needs BPB + FATs + root dir + a few data sectors.
        int imageActualSectors = dataStartSector + 10;
        byte[] image = new byte[imageActualSectors * bytesPerSector];

        // BPB at offset 11
        BitConverter.GetBytes(bytesPerSector).CopyTo(image, 11);
        image[13] = sectorsPerCluster;
        BitConverter.GetBytes(reservedSectors).CopyTo(image, 14);
        image[16] = numberOfFats;
        BitConverter.GetBytes(rootDirEntries).CopyTo(image, 17);
        BitConverter.GetBytes(totalSectors16).CopyTo(image, 19);
        image[21] = 0xF8;
        BitConverter.GetBytes(sectorsPerFat).CopyTo(image, 22);
        BitConverter.GetBytes((ushort)63).CopyTo(image, 24);
        BitConverter.GetBytes((ushort)255).CopyTo(image, 26);
        BitConverter.GetBytes(0u).CopyTo(image, 28);
        BitConverter.GetBytes(0u).CopyTo(image, 32);
        image[38] = 0x29;
        Encoding.ASCII.GetBytes("FAT16      ").CopyTo(image, 43);

        // FAT1 at sector reservedSectors
        int fat1Offset = reservedSectors * bytesPerSector;
        // FAT[0] = 0xFFF8 (media), FAT[1] = 0xFFFF
        image[fat1Offset] = 0xF8;
        image[fat1Offset + 1] = 0xFF;
        image[fat1Offset + 2] = 0xFF;
        image[fat1Offset + 3] = 0xFF;

        if (fileContent != null) {
            // FAT[2] = 0xFFFF (end of chain for file in cluster 2)
            image[fat1Offset + 4] = 0xFF;
            image[fat1Offset + 5] = 0xFF;

            // Root dir at sector (reservedSectors + numberOfFats * sectorsPerFat)
            int rootDirOffset = (reservedSectors + numberOfFats * sectorsPerFat) * bytesPerSector;
            Encoding.ASCII.GetBytes("TEST    ").CopyTo(image, rootDirOffset);
            Encoding.ASCII.GetBytes("TXT").CopyTo(image, rootDirOffset + 8);
            image[rootDirOffset + 11] = 0x20;
            BitConverter.GetBytes((ushort)2).CopyTo(image, rootDirOffset + 26);
            BitConverter.GetBytes((uint)fileContent.Length).CopyTo(image, rootDirOffset + 28);

            // File data at cluster 2
            int dataOffset = dataStartSector * bytesPerSector;
            fileContent.AsSpan().CopyTo(image.AsSpan(dataOffset));
        }

        return image;
    }

    private static byte[] CreateMinimalFat32Image() {
        byte[] image = new byte[512];
        // BPB
        BitConverter.GetBytes((ushort)512).CopyTo(image, 11);
        image[13] = 8;
        BitConverter.GetBytes((ushort)32).CopyTo(image, 14);
        image[16] = 2;
        BitConverter.GetBytes((ushort)0).CopyTo(image, 17);
        BitConverter.GetBytes((ushort)0).CopyTo(image, 19);
        image[21] = 0xF8;
        BitConverter.GetBytes((ushort)0).CopyTo(image, 22);
        BitConverter.GetBytes((ushort)63).CopyTo(image, 24);
        BitConverter.GetBytes((ushort)255).CopyTo(image, 26);
        BitConverter.GetBytes(0u).CopyTo(image, 28);
        BitConverter.GetBytes(2097152u).CopyTo(image, 32);
        // FAT32 extended BPB
        BitConverter.GetBytes(2048u).CopyTo(image, 36);
        BitConverter.GetBytes(2u).CopyTo(image, 44);
        image[66] = 0x29;
        Encoding.ASCII.GetBytes("NO NAME    ").CopyTo(image, 71);
        return image;
    }
}
