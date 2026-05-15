namespace Spice86.Storage.Tests.CdRom;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

/// <summary>
/// TDD tests for ISO 9660 Joliet supplementary volume descriptor handling
/// (Phase 3, atom 1). Covers VD-set traversal, Joliet escape-sequence detection,
/// UCS-2 big-endian volume identifier decoding, and UCS-2 BE root directory
/// reading without breaking the existing Primary Volume Descriptor path.
/// </summary>
public sealed class IsoJolietTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public void IsoImage_PvdOnly_ExposesNoJolietVolume()
    {
        // Arrange
        byte[] image = new Iso9660ImageBuilder()
            .WithVolumeIdentifier("PVDONLY")
            .WithFile("HELLO.TXT", jolietName: "Hello.txt", contents: Encoding.ASCII.GetBytes("hi"))
            .Build();
        string path = WriteTempIso(image);

        // Act
        using IsoImage iso = new(path);

        // Assert
        iso.PrimaryVolume.VolumeIdentifier.Should().Be("PVDONLY");
        iso.JolietVolume.Should().BeNull();
    }

    [Fact]
    public void IsoImage_WithJolietSvd_ExposesJolietVolumeIdentifierDecodedFromUcs2BigEndian()
    {
        // Arrange
        byte[] image = new Iso9660ImageBuilder()
            .WithVolumeIdentifier("PRIMARY")
            .WithJolietVolumeIdentifier("Dos Game Long")
            .WithFile("README.TXT", jolietName: "Read Me.txt", contents: Encoding.ASCII.GetBytes("ok"))
            .Build();
        string path = WriteTempIso(image);

        // Act
        using IsoImage iso = new(path);

        // Assert
        iso.PrimaryVolume.VolumeIdentifier.Should().Be("PRIMARY");
        iso.JolietVolume.Should().NotBeNull();
        iso.JolietVolume!.VolumeIdentifier.Should().Be("Dos Game Long");
        iso.JolietVolume.IsJoliet.Should().BeTrue();
    }

    [Fact]
    public void IsoImage_WithJolietSvd_ReadJolietRootDirectory_DecodesUcs2BigEndianLongNames()
    {
        // Arrange
        byte[] image = new Iso9660ImageBuilder()
            .WithVolumeIdentifier("PRIMARY")
            .WithJolietVolumeIdentifier("PRIMARY")
            .WithFile("README.TXT", jolietName: "Read Me Long Filename.txt", contents: Encoding.ASCII.GetBytes("payload-1"))
            .WithFile("CONFIG.SYS", jolietName: "config.sys", contents: Encoding.ASCII.GetBytes("payload-2"))
            .Build();
        string path = WriteTempIso(image);
        using IsoImage iso = new(path);

        // Act
        IReadOnlyList<IsoDirectoryRecord> entries = iso.ReadJolietRootDirectory();

        // Assert
        List<string> names = new();
        foreach (IsoDirectoryRecord record in entries)
        {
            if (record.IsDirectory)
            {
                continue;
            }
            names.Add(record.Name);
        }
        names.Should().BeEquivalentTo(new[] { "Read Me Long Filename.txt", "config.sys" });
    }

    [Fact]
    public void IsoImage_WithJolietSvd_PrimaryRootDirectory_StillReadsAsciiNames()
    {
        // Arrange
        byte[] image = new Iso9660ImageBuilder()
            .WithVolumeIdentifier("PRIMARY")
            .WithJolietVolumeIdentifier("PRIMARY")
            .WithFile("README.TXT", jolietName: "Read Me.txt", contents: Encoding.ASCII.GetBytes("payload"))
            .Build();
        string path = WriteTempIso(image);
        using IsoImage iso = new(path);
        byte[] rootSector = new byte[iso.PrimaryVolume.RootDirectorySize];
        iso.Read(iso.PrimaryVolume.RootDirectoryLba, rootSector, CdSectorMode.CookedData2048);

        // Act
        List<string> primaryNames = new();
        int offset = 0;
        while (offset < rootSector.Length)
        {
            IsoDirectoryRecord? record = IsoDirectoryRecord.ParseNullable(rootSector.AsSpan(offset));
            if (record is null)
            {
                break;
            }
            if (!record.IsDirectory)
            {
                primaryNames.Add(record.Name);
            }
            offset += rootSector[offset];
        }

        // Assert: Primary path stays untouched (ASCII upper-cased, version stripped).
        primaryNames.Should().BeEquivalentTo(new[] { "README.TXT" });
    }

    [Fact]
    public void IsoImage_NonJolietSupplementaryVolume_IsIgnored()
    {
        // Arrange: Build a PVD-only image, then patch sector 17 to be a non-Joliet SVD
        // (escape sequence does not match any Joliet level).
        byte[] image = new Iso9660ImageBuilder()
            .WithVolumeIdentifier("PRIMARY")
            .WithFile("HELLO.TXT", jolietName: "Hello.txt", contents: Encoding.ASCII.GetBytes("hi"))
            .Build();
        // Sector 17 was previously the VD terminator; insert a non-Joliet SVD by relocating
        // the terminator forward by one sector.
        byte[] enlarged = new byte[image.Length + 2048];
        Buffer.BlockCopy(image, 0, enlarged, 0, 17 * 2048); // up to and including PVD
        // sector 17: synthetic non-Joliet SVD
        Span<byte> svd = enlarged.AsSpan(17 * 2048, 2048);
        svd[0] = 0x02;
        Encoding.ASCII.GetBytes("CD001", svd.Slice(1, 5));
        svd[6] = 0x01;
        // escape sequence at offset 88 — bogus bytes (not 0x25/0x2F/0x40-45)
        svd[88] = 0x11;
        svd[89] = 0x22;
        svd[90] = 0x33;
        // sectors 18+ shifted from original 17+
        Buffer.BlockCopy(image, 17 * 2048, enlarged, 18 * 2048, image.Length - 17 * 2048);
        string path = WriteTempIso(enlarged);

        // Act
        using IsoImage iso = new(path);

        // Assert
        iso.PrimaryVolume.VolumeIdentifier.Should().Be("PRIMARY");
        iso.JolietVolume.Should().BeNull();
    }

    [Fact]
    public void IsoImage_WithJolietSvd_ReadJolietRootDirectory_FileContentsRecoverableViaExtentLba()
    {
        // Arrange
        byte[] payload = Encoding.ASCII.GetBytes("Joliet-payload-marker");
        byte[] image = new Iso9660ImageBuilder()
            .WithVolumeIdentifier("PRIMARY")
            .WithJolietVolumeIdentifier("PRIMARY")
            .WithFile("DATA.BIN", jolietName: "Data File.bin", contents: payload)
            .Build();
        string path = WriteTempIso(image);
        using IsoImage iso = new(path);
        IReadOnlyList<IsoDirectoryRecord> entries = iso.ReadJolietRootDirectory();
        IsoDirectoryRecord? dataEntry = null;
        foreach (IsoDirectoryRecord record in entries)
        {
            if (record.Name == "Data File.bin")
            {
                dataEntry = record;
                break;
            }
        }
        dataEntry.Should().NotBeNull();

        // Act
        byte[] sector = new byte[2048];
        iso.Read(dataEntry!.ExtentLba, sector, CdSectorMode.CookedData2048);

        // Assert
        sector.AsSpan(0, payload.Length).ToArray().Should().BeEquivalentTo(payload);
        dataEntry.DataLength.Should().Be(payload.Length);
    }

    private string WriteTempIso(byte[] image)
    {
        string path = Path.Combine(Path.GetTempPath(), $"spice86_iso_{Guid.NewGuid():N}.iso");
        File.WriteAllBytes(path, image);
        _tempFiles.Add(path);
        return path;
    }
}
