namespace Spice86.Tests.Emulator.Devices.CdRom;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom;

using System;
using System.IO;
using System.Text;

using Xunit;

/// <summary>
/// Tests for <see cref="VirtualIsoImage"/> — builds an ISO 9660 image from a host directory.
/// </summary>
public sealed class VirtualIsoImageTests : IDisposable {
    private const int SectorSize = 2048;

    private readonly string _testDir;

    public VirtualIsoImageTests() {
        _testDir = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose() {
        if (Directory.Exists(_testDir)) {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void PrimaryVolume_VolumeLabel_MatchesSuppliedLabel() {
        // Arrange & Act
        VirtualIsoImage iso = new(_testDir, "TESTLABEL");

        // Assert
        iso.PrimaryVolume.VolumeIdentifier.Should().Be("TESTLABEL");
    }

    [Fact]
    public void PrimaryVolume_RootDirectoryLba_Is20() {
        // Arrange & Act
        VirtualIsoImage iso = new(_testDir, "DISC");

        // Assert
        iso.PrimaryVolume.RootDirectoryLba.Should().Be(20, "root directory always occupies LBA 20 in virtual ISO layout");
    }

    [Fact]
    public void PrimaryVolume_LogicalBlockSize_Is2048() {
        // Arrange & Act
        VirtualIsoImage iso = new(_testDir, "DISC");

        // Assert
        iso.PrimaryVolume.LogicalBlockSize.Should().Be(SectorSize);
    }

    [Fact]
    public void TotalSectors_EmptyDirectory_AtLeast21() {
        // Arrange & Act
        VirtualIsoImage iso = new(_testDir, "DISC");

        // Assert
        iso.TotalSectors.Should().BeGreaterThanOrEqualTo(21, "system area (0-15) + PVD + VDST + path tables + root dir = sectors 0-20, minimum 21 sectors");
    }

    [Fact]
    public void Read_PvdSector_StartsWithCorrectSignature() {
        // Arrange
        VirtualIsoImage iso = new(_testDir, "DISC");
        byte[] buffer = new byte[SectorSize];

        // Act
        iso.Read(16, buffer, CdSectorMode.CookedData2048);

        // Assert — PVD starts with descriptor type 0x01 then "CD001"
        buffer[0].Should().Be(0x01, "PVD descriptor type is 1");
        Encoding.ASCII.GetString(buffer, 1, 5).Should().Be("CD001");
    }

    [Fact]
    public void Read_WithFile_FileDataReadableAtExpectedLba() {
        // Arrange
        byte[] expected = Encoding.ASCII.GetBytes("ISO CONTENT");
        File.WriteAllBytes(Path.Combine(_testDir, "DATA.TXT"), expected);
        VirtualIsoImage iso = new(_testDir, "DISC");

        // Root dir sector at LBA 20 contains directory records for "." ".." and "DATA.TXT;1"
        byte[] rootDir = new byte[SectorSize];
        iso.Read(20, rootDir, CdSectorMode.CookedData2048);

        // Find DATA.TXT record — scan directory records for any name starting with "DATA.TXT"
        int fileLba = -1;
        int pos = 0;
        while (pos < rootDir.Length) {
            int recLen = rootDir[pos];
            if (recLen == 0) {
                break;
            }
            int nameLen = rootDir[pos + 32];
            string name = Encoding.ASCII.GetString(rootDir, pos + 33, nameLen);
            if (name.StartsWith("DATA.TXT", StringComparison.OrdinalIgnoreCase)) {
                fileLba = rootDir[pos + 2] | (rootDir[pos + 3] << 8) | (rootDir[pos + 4] << 16) | (rootDir[pos + 5] << 24);
                break;
            }
            pos += recLen;
        }
        fileLba.Should().BeGreaterThanOrEqualTo(21, "file data starts at LBA 21 or later");

        // Act
        byte[] sector = new byte[SectorSize];
        iso.Read(fileLba, sector, CdSectorMode.CookedData2048);

        // Assert
        sector[..expected.Length].Should().Equal(expected);
    }

    [Fact]
    public void Tracks_ContainsSingleDataTrack() {
        // Arrange & Act
        VirtualIsoImage iso = new(_testDir, "DISC");

        // Assert
        iso.Tracks.Should().HaveCount(1);
        iso.Tracks[0].IsAudio.Should().BeFalse();
    }

    [Fact]
    public void ImagePath_ReturnsSourceDirectory() {
        // Arrange & Act
        VirtualIsoImage iso = new(_testDir, "DISC");

        // Assert
        iso.ImagePath.Should().Be(_testDir);
    }

    [Fact]
    public void RootDirectory_ContainsSubdirectoryEntry_WithDirectoryFlag() {
        // Arrange
        string subDir = Path.Combine(_testDir, "SUBDIR");
        Directory.CreateDirectory(subDir);
        VirtualIsoImage iso = new(_testDir, "DISC");

        // Act
        DirectoryRecord? sub = FindRecord(iso, lba: 20, name: "SUBDIR");

        // Assert
        sub.Should().NotBeNull("the root directory must list SUBDIR as an entry");
        sub.IsDirectory.Should().BeTrue("byte 25 bit 0x02 must be set for directory entries");
        sub.Lba.Should().BeGreaterThan(20, "subdirectory contents are stored after the root directory sector");
    }

    [Fact]
    public void Subdirectory_ContainsDotAndDotDotEntries_PointingToSelfAndParent() {
        // Arrange
        string subDir = Path.Combine(_testDir, "SUBDIR");
        Directory.CreateDirectory(subDir);
        VirtualIsoImage iso = new(_testDir, "DISC");
        DirectoryRecord? sub = FindRecord(iso, lba: 20, name: "SUBDIR");
        sub.Should().NotBeNull();

        // Act
        byte[] subSector = new byte[SectorSize];
        iso.Read(sub.Lba, subSector, CdSectorMode.CookedData2048);

        // Assert — first two records are "." (self) and ".." (parent = root LBA 20)
        int dotNameLen = subSector[32];
        dotNameLen.Should().Be(1);
        subSector[33].Should().Be(0x00, "'.' record uses identifier 0x00");
        int dotLba = ReadLeInt32(subSector, 2);
        dotLba.Should().Be(sub.Lba, "the '.' entry must point at the subdirectory itself");

        int dotRecLen = subSector[0];
        int dotDotNameLen = subSector[dotRecLen + 32];
        dotDotNameLen.Should().Be(1);
        subSector[dotRecLen + 33].Should().Be(0x01, "'..' record uses identifier 0x01");
        int dotDotLba = ReadLeInt32(subSector, dotRecLen + 2);
        dotDotLba.Should().Be(20, "the '..' entry of a top-level subdirectory must point at the root directory");
    }

    [Fact]
    public void FileInsideSubdirectory_IsReadableViaSubdirectoryRecord() {
        // Arrange
        string subDir = Path.Combine(_testDir, "SUBDIR");
        Directory.CreateDirectory(subDir);
        byte[] expected = Encoding.ASCII.GetBytes("NESTED FILE CONTENT");
        File.WriteAllBytes(Path.Combine(subDir, "INNER.TXT"), expected);
        VirtualIsoImage iso = new(_testDir, "DISC");
        DirectoryRecord? sub = FindRecord(iso, lba: 20, name: "SUBDIR");
        sub.Should().NotBeNull();

        // Act
        DirectoryRecord? inner = FindRecord(iso, lba: sub.Lba, name: "INNER.TXT");

        // Assert
        inner.Should().NotBeNull("the subdirectory must list INNER.TXT");
        inner.IsDirectory.Should().BeFalse();
        byte[] sector = new byte[SectorSize];
        iso.Read(inner.Lba, sector, CdSectorMode.CookedData2048);
        sector[..expected.Length].Should().Equal(expected);
    }

    [Fact]
    public void NestedSubdirectory_IsTraversable_AtDepthTwo() {
        // Arrange
        string level1 = Path.Combine(_testDir, "LEVEL1");
        string level2 = Path.Combine(level1, "LEVEL2");
        Directory.CreateDirectory(level2);
        byte[] expected = Encoding.ASCII.GetBytes("DEEP");
        File.WriteAllBytes(Path.Combine(level2, "DEEP.TXT"), expected);
        VirtualIsoImage iso = new(_testDir, "DISC");

        // Act
        DirectoryRecord? l1 = FindRecord(iso, lba: 20, name: "LEVEL1");
        l1.Should().NotBeNull();
        DirectoryRecord? l2 = FindRecord(iso, lba: l1.Lba, name: "LEVEL2");
        l2.Should().NotBeNull();
        DirectoryRecord? deep = FindRecord(iso, lba: l2.Lba, name: "DEEP.TXT");

        // Assert
        deep.Should().NotBeNull();
        byte[] sector = new byte[SectorSize];
        iso.Read(deep.Lba, sector, CdSectorMode.CookedData2048);
        sector[..expected.Length].Should().Equal(expected);
    }

    private static DirectoryRecord? FindRecord(VirtualIsoImage iso, int lba, string name) {
        byte[] sector = new byte[SectorSize];
        iso.Read(lba, sector, CdSectorMode.CookedData2048);
        int pos = 0;
        while (pos < sector.Length) {
            int recLen = sector[pos];
            if (recLen == 0) {
                break;
            }
            int nameLen = sector[pos + 32];
            string recordName = Encoding.ASCII.GetString(sector, pos + 33, nameLen);
            // Strip trailing ";1" version suffix on files.
            int semicolon = recordName.IndexOf(';');
            if (semicolon >= 0) {
                recordName = recordName[..semicolon];
            }
            if (string.Equals(recordName, name, StringComparison.OrdinalIgnoreCase)) {
                int recordLba = ReadLeInt32(sector, pos + 2);
                bool isDir = (sector[pos + 25] & 0x02) != 0;
                return new DirectoryRecord(recordLba, isDir);
            }
            pos += recLen;
        }
        return null;
    }

    private static int ReadLeInt32(byte[] data, int offset) {
        return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
    }

    private sealed record DirectoryRecord(int Lba, bool IsDirectory);
}
