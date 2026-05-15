namespace Spice86.Storage.Tests.CdRom.Audio;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom;

using System;
using System.IO;

using Xunit;

/// <summary>
/// TDD tests verifying that <see cref="CueSheetParser"/> correctly identifies all
/// compressed-audio file-type tokens introduced in Phase 4 atom 2.
/// </summary>
public sealed class CueSheetParserAudioFileTypeTests : IDisposable {
    private readonly string _tempDirectory;

    public CueSheetParserAudioFileTypeTests() {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Spice86_CueParserAudio_" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("MP3", CueFileType.Mp3)]
    [InlineData("FLAC", CueFileType.Flac)]
    [InlineData("OGG", CueFileType.Ogg)]
    [InlineData("OPUS", CueFileType.Opus)]
    [InlineData("AIFF", CueFileType.Aiff)]
    [InlineData("MOTOROLA", CueFileType.Motorola)]
    public void CueSheetParser_ExtractsAudioFileType(string token, CueFileType expected) {
        // Arrange
        string cuePath = WriteCueSheet("disc.cue",
            $"FILE \"track.bin\" {token}",
            "  TRACK 01 AUDIO",
            "    INDEX 01 00:00:00");

        // Act
        CueSheet sheet = new CueSheetParser().Parse(cuePath);

        // Assert
        sheet.Entries.Should().HaveCount(1);
        sheet.Entries[0].FileType.Should().Be(expected);
    }

    private string WriteCueSheet(string name, params string[] lines) {
        string path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        return path;
    }
}
