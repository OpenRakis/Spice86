namespace Spice86.Tests.Emulator.Devices.CdRom;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.CdRom.Image;

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
    public void TotalSectors_EmptyDirectory_AtLeast22() {
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
}
