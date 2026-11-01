namespace Spice86.Tests.Dos.Structures;

using FluentAssertions;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.OperatingSystem.Structures;

using Xunit;

public class DosVolumeInfoTest {
    private const byte Sentinel = 0x7E;

    private static DosVolumeInfo CreateStructure(byte[] buffer) {
        Array.Fill(buffer, Sentinel);
        return new DosVolumeInfo(new ByteArrayReaderWriter(buffer), 0);
    }

    [Fact]
    public void VolumeLabel_WritesASpacePaddedElevenByteField() {
        // Arrange
        byte[] buffer = new byte[0x20];
        DosVolumeInfo volumeInfo = CreateStructure(buffer);

        // Act
        volumeInfo.VolumeLabel = "SPICE86";

        // Assert
        buffer[0x06..0x11].Should().Equal("SPICE86    "u8.ToArray());
        buffer[0x11].Should().Be(Sentinel, "the byte after the 11-byte label field is not part of it");
    }

    [Fact]
    public void VolumeLabel_WritesOnlyTheFirstElevenBytesOfALongerLabel() {
        // Arrange
        byte[] buffer = new byte[0x20];
        DosVolumeInfo volumeInfo = CreateStructure(buffer);

        // Act
        volumeInfo.VolumeLabel = "ABCDEFGHIJKLMN";

        // Assert
        buffer[0x06..0x11].Should().Equal("ABCDEFGHIJK"u8.ToArray());
        buffer[0x11].Should().Be(Sentinel, "the byte after the 11-byte label field is not part of it");
    }

    [Fact]
    public void FileSystemType_WritesASpacePaddedEightByteField() {
        // Arrange
        byte[] buffer = new byte[0x20];
        DosVolumeInfo volumeInfo = CreateStructure(buffer);

        // Act
        volumeInfo.FileSystemType = "FAT16";

        // Assert
        buffer[0x11..0x19].Should().Equal("FAT16   "u8.ToArray());
        buffer[0x19].Should().Be(Sentinel, "the byte after the 8-byte file system type field is not part of it");
    }

    [Fact]
    public void VolumeLabel_ReadsBackTheSpacePaddedField() {
        // Arrange
        byte[] buffer = new byte[0x20];
        DosVolumeInfo volumeInfo = CreateStructure(buffer);

        // Act
        volumeInfo.VolumeLabel = "SPICE86";

        // Assert
        volumeInfo.VolumeLabel.Should().Be("SPICE86    ");
    }

    [Fact]
    public void FileSystemType_ReadsBackTheSpacePaddedField() {
        // Arrange
        byte[] buffer = new byte[0x20];
        DosVolumeInfo volumeInfo = CreateStructure(buffer);

        // Act
        volumeInfo.FileSystemType = "FAT16";

        // Assert
        volumeInfo.FileSystemType.Should().Be("FAT16   ");
    }
}
