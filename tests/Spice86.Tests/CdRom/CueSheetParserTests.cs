namespace Spice86.Tests.CdRom;

using System;
using System.IO;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom;

using Xunit;

public sealed class CueSheetParserTests : IDisposable {
    private readonly string _tempDirectory;

    public CueSheetParserTests() {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Spice86_CueSheetParserTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Parse_RelativeQuotedFile_ResolvesAgainstCueDirectory() {
        string binDirectory = Path.Combine(_tempDirectory, "images");
        Directory.CreateDirectory(binDirectory);
        string binPath = Path.Combine(binDirectory, "track 01.bin");
        File.WriteAllBytes(binPath, new byte[2352]);

        string cuePath = Path.Combine(_tempDirectory, "disc.cue");
        string cueText =
            "FILE \"images/track 01.bin\" BINARY" + Environment.NewLine +
            "TRACK 01 MODE1/2352" + Environment.NewLine +
            "INDEX 01 00:02:00" + Environment.NewLine;
        File.WriteAllText(cuePath, cueText);

        CueSheetParser parser = new CueSheetParser();

        CueSheet sheet = parser.Parse(cuePath);

        sheet.Entries.Should().HaveCount(1);
        sheet.Entries[0].FileName.Should().Be(Path.GetFullPath(binPath));
    }

    [Fact]
    public void Parse_MalformedMsf_ThrowsInvalidDataException() {
        string cuePath = Path.Combine(_tempDirectory, "bad.cue");
        string cueText =
            "FILE \"disc.bin\" BINARY" + Environment.NewLine +
            "TRACK 01 MODE1/2352" + Environment.NewLine +
            "INDEX 01 00:AA:00" + Environment.NewLine;
        File.WriteAllText(cuePath, cueText);

        CueSheetParser parser = new CueSheetParser();

        Action act = () => parser.Parse(cuePath);

        act.Should().Throw<InvalidDataException>();
    }

    public void Dispose() {
        if (Directory.Exists(_tempDirectory)) {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
