namespace Spice86.Tests.CdRom;

using System;
using System.IO;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom;

using Xunit;

public sealed class CueBinImageTests : IDisposable {
    private const int RawSectorSize = 2352;
    private readonly string _tempDirectory;

    public CueBinImageTests() {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Spice86_CueBinImageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Constructor_MultiTrackCue_UsesIndexFramesForStartAndLength() {
        string binPath = Path.Combine(_tempDirectory, "disc.bin");
        int totalSectors = 500;
        File.WriteAllBytes(binPath, new byte[totalSectors * RawSectorSize]);

        string cuePath = Path.Combine(_tempDirectory, "disc.cue");
        string cueText =
            "FILE \"disc.bin\" BINARY" + Environment.NewLine +
            "TRACK 01 MODE1/2352" + Environment.NewLine +
            "INDEX 01 00:02:00" + Environment.NewLine +
            "TRACK 02 AUDIO" + Environment.NewLine +
            "INDEX 01 00:04:00" + Environment.NewLine;
        File.WriteAllText(cuePath, cueText);

        using CueBinImage image = new CueBinImage(cuePath);

        image.Tracks.Should().HaveCount(2);
        image.Tracks[0].StartLba.Should().Be(0);
        image.Tracks[0].LengthSectors.Should().Be(150);
        image.Tracks[1].StartLba.Should().Be(150);
        image.Tracks[1].LengthSectors.Should().Be(350);
    }

    public void Dispose() {
        if (Directory.Exists(_tempDirectory)) {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
