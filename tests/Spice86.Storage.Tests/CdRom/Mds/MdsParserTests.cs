namespace Spice86.Storage.Tests.CdRom.Mds;

using System;
using System.IO;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom.Mds;

using Xunit;

/// <summary>Tests for <see cref="MdsParser"/>.</summary>
public sealed class MdsParserTests
{
    [Fact]
    public void Parse_SingleDataTrack_ProducesOneTrackWithExpectedFields()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "single.mds");
        new MdsImageBuilder()
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 100)
            .WithMdfFilename("single.mdf")
            .WriteToDisk(mdsPath);

        // Act
        MdsDiscDescriptor descriptor = new MdsParser().ParseFile(mdsPath);

        // Assert
        descriptor.Tracks.Should().HaveCount(1);
        MdsTrack track = descriptor.Tracks[0];
        track.Number.Should().Be(1);
        track.Mode.Should().Be(MdsTrackMode.Mode1Data);
        track.SectorSize.Should().Be(2048);
        track.StartSector.Should().Be(0);
        track.LengthSectors.Should().Be(100);
        track.MdfFilename.Should().Be("single.mdf");
    }

    [Fact]
    public void Parse_TwoTracksDataThenAudio_PreservesOrderAndModes()
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
        MdsDiscDescriptor descriptor = new MdsParser().ParseFile(mdsPath);

        // Assert
        descriptor.Tracks.Should().HaveCount(2);
        descriptor.Tracks[0].Mode.Should().Be(MdsTrackMode.Mode1Data);
        descriptor.Tracks[1].Mode.Should().Be(MdsTrackMode.Audio);
        descriptor.Tracks[1].SectorSize.Should().Be(2352);
        descriptor.Tracks[1].StartSector.Should().Be(1000);
    }

    [Fact]
    public void Parse_NonTrackBlockBeforeTracks_IsSkipped()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "leadout.mds");
        new MdsImageBuilder()
            .WithLeadOutMarker()
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 50)
            .WithMdfFilename("leadout.mdf")
            .WriteToDisk(mdsPath);

        // Act
        MdsDiscDescriptor descriptor = new MdsParser().ParseFile(mdsPath);

        // Assert
        descriptor.Tracks.Should().HaveCount(1);
        descriptor.Tracks[0].Number.Should().Be(1);
    }

    [Fact]
    public void Parse_InvalidSignature_Throws()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "bad-sig.mds");
        new MdsImageBuilder()
            .WithInvalidSignature()
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 10)
            .WriteToDisk(mdsPath);

        // Act
        Action act = () => new MdsParser().ParseFile(mdsPath);

        // Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*signature*");
    }

    [Fact]
    public void Parse_UnsupportedMajorVersion_Throws()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "bad-ver.mds");
        new MdsImageBuilder()
            .WithMajorVersion(2)
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 10)
            .WriteToDisk(mdsPath);

        // Act
        Action act = () => new MdsParser().ParseFile(mdsPath);

        // Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*version*");
    }

    [Fact]
    public void Parse_ZeroSessionsHeader_Throws()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "zero-sessions.mds");
        new MdsImageBuilder()
            .WithSessionCount(0)
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 10)
            .WriteToDisk(mdsPath);

        // Act
        Action act = () => new MdsParser().ParseFile(mdsPath);

        // Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*sessions*");
    }

    [Fact]
    public void Parse_WideCharFilename_DecodedAsUtf16()
    {
        // Arrange
        string dir = CreateTempDir();
        string mdsPath = Path.Combine(dir, "wide.mds");
        new MdsImageBuilder()
            .WithDataTrack(trackNumber: 1, startSector: 0, lengthSectors: 10)
            .WithMdfFilename("WideName.mdf")
            .WithWideFilename()
            .WriteToDisk(mdsPath);

        // Act
        MdsDiscDescriptor descriptor = new MdsParser().ParseFile(mdsPath);

        // Assert
        descriptor.Tracks[0].MdfFilename.Should().Be("WideName.mdf");
    }

    private static string CreateTempDir()
    {
        string root = Path.Combine(Path.GetTempPath(), "spice86-mds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
