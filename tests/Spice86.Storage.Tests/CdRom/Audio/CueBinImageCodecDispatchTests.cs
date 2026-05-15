namespace Spice86.Storage.Tests.CdRom.Audio;

using FluentAssertions;

using NSubstitute;

using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Shared.Emulator.Storage.CdRom.Audio;

using System;
using System.Collections.Generic;
using System.IO;

using Xunit;

/// <summary>
/// TDD tests verifying that <see cref="CueBinImage"/> routes non-WAVE audio file
/// types through the injected <see cref="CompositeAudioCodecFactory"/> instead of
/// reading the file as raw bytes (Phase 4, atom 2).
/// </summary>
public sealed class CueBinImageCodecDispatchTests : IDisposable {
    private const int RawSectorSize = 2352;
    private readonly string _tempDirectory;

    public CueBinImageCodecDispatchTests() {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Spice86_CueCodecDispatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc/>
    public void Dispose() {
        try {
            if (Directory.Exists(_tempDirectory)) {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        } catch (IOException) {
            // best-effort cleanup
        }
    }

    [Fact]
    public void CueBinImage_FileMp3Audio_RoutesThroughInjectedCodecFactory() {
        // Arrange
        string mp3Path = Path.Combine(_tempDirectory, "track01.mp3");
        File.WriteAllBytes(mp3Path, new byte[] { 0xFF, 0xFB, 0x00 }); // pretend MP3 header
        byte[] pcm = new byte[RawSectorSize * 2];
        for (int i = 0; i < pcm.Length; i++) {
            pcm[i] = (byte)((i + 0x10) & 0xFF);
        }
        IAudioCodec fakeCodec = Substitute.For<IAudioCodec>();
        fakeCodec.OpenAsCdda(mp3Path).Returns(new MemoryDataSource(pcm));
        IAudioCodecFactory fakeFactory = Substitute.For<IAudioCodecFactory>();
        fakeFactory.CanHandle(CueFileType.Mp3, Arg.Any<string>()).Returns(true);
        fakeFactory.Create().Returns(fakeCodec);
        CompositeAudioCodecFactory composite = new(fakeFactory);
        string cuePath = WriteCueSheet("disc.cue",
            "FILE \"track01.mp3\" MP3",
            "  TRACK 01 AUDIO",
            "    INDEX 01 00:02:00");

        using CueBinImage image = new(cuePath, composite);

        // Act
        byte[] readSector0 = new byte[RawSectorSize];
        image.Read(0, readSector0, CdSectorMode.AudioRaw2352);

        // Assert
        image.Tracks.Should().HaveCount(1);
        image.Tracks[0].IsAudio.Should().BeTrue();
        fakeCodec.Received(1).OpenAsCdda(mp3Path);
        readSector0.AsSpan(0, RawSectorSize).ToArray()
            .Should().BeEquivalentTo(pcm.AsSpan(0, RawSectorSize).ToArray());
    }

    [Fact]
    public void CueBinImage_FileMp3Audio_DisposesCodecCreatedDuringConstruction() {
        // Arrange
        string mp3Path = Path.Combine(_tempDirectory, "track.mp3");
        File.WriteAllBytes(mp3Path, new byte[] { 0xFF, 0xFB, 0x00 });
        DisposableSpyCodec spy = new(new MemoryDataSource(new byte[RawSectorSize]));
        IAudioCodecFactory fakeFactory = Substitute.For<IAudioCodecFactory>();
        fakeFactory.CanHandle(CueFileType.Mp3, Arg.Any<string>()).Returns(true);
        fakeFactory.Create().Returns(spy);
        CompositeAudioCodecFactory composite = new(fakeFactory);
        string cuePath = WriteCueSheet("disc.cue",
            "FILE \"track.mp3\" MP3",
            "  TRACK 01 AUDIO",
            "    INDEX 01 00:02:00");
        CueBinImage image = new(cuePath, composite);

        // Act
        image.Dispose();

        // Assert
        spy.DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public void CueBinImage_FileMp3Audio_NoCodecAvailable_ThrowsNotSupportedException() {
        // Arrange
        string mp3Path = Path.Combine(_tempDirectory, "track.mp3");
        File.WriteAllBytes(mp3Path, new byte[] { 0xFF, 0xFB });
        IAudioCodecFactory neverHandles = Substitute.For<IAudioCodecFactory>();
        neverHandles.CanHandle(Arg.Any<CueFileType>(), Arg.Any<string>()).Returns(false);
        CompositeAudioCodecFactory composite = new(neverHandles);
        string cuePath = WriteCueSheet("disc.cue",
            "FILE \"track.mp3\" MP3",
            "  TRACK 01 AUDIO",
            "    INDEX 01 00:02:00");

        // Act
        Action act = () => _ = new CueBinImage(cuePath, composite);

        // Assert
        act.Should().Throw<NotSupportedException>();
    }

    private string WriteCueSheet(string name, params string[] lines) {
        string path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        return path;
    }

    private sealed class DisposableSpyCodec : IAudioCodec, IDisposable {
        private readonly IDataSource _source;
        public int DisposeCallCount { get; private set; }

        public DisposableSpyCodec(IDataSource source) {
            _source = source;
        }

        public IDataSource OpenAsCdda(string filePath) {
            return _source;
        }

        public void Dispose() {
            DisposeCallCount++;
        }
    }
}
