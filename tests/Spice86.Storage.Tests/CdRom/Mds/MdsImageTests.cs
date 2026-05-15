namespace Spice86.Storage.Tests.CdRom.Mds;

using System;
using System.IO;
using System.Linq;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom;

using Xunit;

/// <summary>Tests for <see cref="MdsImage"/> and the factory dispatch path.</summary>
public sealed class MdsImageTests
{
    [Fact]
    public void Ctor_SingleDataTrack_AppendsSyntheticLeadOut()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "single.mds");
        new MdsImageBuilder()
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 100)
            .WithMdfFilename("single.mdf")
            .WriteToDisk(mdsPath);

        // Act
        using MdsImage image = new MdsImage(mdsPath);

        // Assert
        image.Tracks.Should().HaveCount(2);
        image.Tracks[0].Number.Should().Be(1);
        image.Tracks[1].Number.Should().Be(0);
        image.Tracks[1].LengthSectors.Should().Be(0);
        image.Tracks[1].StartLba.Should().Be(100);
    }

    [Fact]
    public void Ctor_DataAndAudioTracks_PreservesLbaBoundaries()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "two.mds");
        new MdsImageBuilder()
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 1000)
            .WithAudioTrack(trackNumber: 2, startSector: 1000, lengthSectors: 500)
            .WithMdfFilename("two.mdf")
            .WriteToDisk(mdsPath);

        // Act
        using MdsImage image = new MdsImage(mdsPath);

        // Assert
        image.Tracks.Where(t => t.LengthSectors > 0).Should().HaveCount(2);
        image.Tracks[0].StartLba.Should().Be(0);
        image.Tracks[0].LengthSectors.Should().Be(1000);
        image.Tracks[1].StartLba.Should().Be(1000);
        image.Tracks[1].IsAudio.Should().BeTrue();
    }

    [Fact]
    public void Read_DataTrack_ExtractsPvdLikeSector()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "pvd.mds");
        new MdsImageBuilder()
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 100)
            .WithMdfFilename("pvd.mdf")
            .WriteToDisk(mdsPath);
        using MdsImage image = new MdsImage(mdsPath);
        byte[] buffer = new byte[2048];

        // Act
        int bytesRead = image.Read(16, buffer, CdSectorMode.CookedData2048);

        // Assert
        bytesRead.Should().Be(2048);
        buffer[0].Should().Be(0x01); // PVD type code
        System.Text.Encoding.ASCII.GetString(buffer, 1, 5).Should().Be("CD001");
    }

    [Fact]
    public void Factory_OpenMds_ReturnsMdsImage()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "factory.mds");
        new MdsImageBuilder()
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 50)
            .WithMdfFilename("factory.mdf")
            .WriteToDisk(mdsPath);

        // Act
        using ICdRomImage image = CdRomImageFactory.Open(mdsPath);

        // Assert
        image.Should().BeOfType<MdsImage>();
    }

    [Fact]
    public void Factory_OpenUnknownExtension_Throws()
    {
        // Arrange
        string dir = CreateTempDir();
        string nopePath = Path.Combine(dir, "nope.xyz");
        File.WriteAllBytes(nopePath, Array.Empty<byte>());

        // Act
        Action act = () => CdRomImageFactory.Open(nopePath);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static string CreateTempDir()
    {
        string root = Path.Combine(Path.GetTempPath(), "spice86-mds-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
