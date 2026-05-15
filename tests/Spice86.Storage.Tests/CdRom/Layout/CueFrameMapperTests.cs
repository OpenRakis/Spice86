namespace Spice86.Storage.Tests.CdRom.Layout;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom;

using System.Collections.Generic;

using Xunit;

/// <summary>
/// TDD tests for <see cref="CueFrameMapper"/> and <see cref="CueTrackLayout"/>
/// (Phase 4 atom 3 / Phase 4c). Verifies INDEX 00 (in-file pregap) accounting,
/// Red Book frame padding, last-track length derivation, and regression of
/// the legacy CUE-without-INDEX-00 path.
/// </summary>
public sealed class CueFrameMapperTests
{
    private const int RawSectorSize = 2352;
    private const string TrackModeAudio = "AUDIO";
    private const string TrackModeMode1Cooked = "MODE1/2048";

    [Fact]
    public void BuildLayout_TwoTracks_NoIndex00_DerivesLbaAndLengthFromNextIndex01()
    {
        // Arrange
        // Track 1 INDEX 01 at 00:02:00 (frame 150). Track 2 INDEX 01 at 01:00:00 (frame 4500).
        CueSheet sheet = SheetFromEntries(
            CreateEntry("track1.bin", CueFileType.Binary, TrackModeMode1Cooked, trackNumber: 1, indexNumber: 1, indexMsf: 150),
            CreateEntry("track1.bin", CueFileType.Binary, TrackModeMode1Cooked, trackNumber: 2, indexNumber: 1, indexMsf: 4500));
        CueFrameMapper mapper = new CueFrameMapper();

        // Act
        IReadOnlyList<CueTrackLayout> layout = mapper.BuildLayout(sheet, _ => 10_000_000L);

        // Assert
        layout.Should().HaveCount(2);
        layout[0].StartLba.Should().Be(0);
        layout[0].LengthSectors.Should().Be(4350);
        layout[0].Index00Frames.Should().BeNull();
        layout[1].StartLba.Should().Be(4350);
        layout[1].Index00Frames.Should().BeNull();
    }

    [Fact]
    public void BuildLayout_NextTrackHasIndex00_ReducesPreviousTrackLengthBySkip()
    {
        // Arrange
        // Track 1 INDEX 01 at 00:02:00 (150). Track 2 INDEX 00 at 00:58:00 (4350), INDEX 01 at 01:00:00 (4500).
        // skip = 4500 - 4350 = 150 frames. Previous track ends at next.Index00.
        // Track 1 length = (4500 - 150) - 0 - 150 = 4200.
        CueSheet sheet = SheetFromEntries(
            CreateEntry("disc.bin", CueFileType.Binary, TrackModeAudio, trackNumber: 1, indexNumber: 1, indexMsf: 150),
            CreateEntry("disc.bin", CueFileType.Binary, TrackModeAudio, trackNumber: 2, indexNumber: 0, indexMsf: 4350),
            CreateEntry("disc.bin", CueFileType.Binary, TrackModeAudio, trackNumber: 2, indexNumber: 1, indexMsf: 4500));
        CueFrameMapper mapper = new CueFrameMapper();

        // Act
        IReadOnlyList<CueTrackLayout> layout = mapper.BuildLayout(sheet, _ => 100_000_000L);

        // Assert
        layout.Should().HaveCount(2);
        layout[0].LengthSectors.Should().Be(4200);
        layout[1].Index00Frames.Should().Be(4350);
        layout[1].StartLba.Should().Be(4350);
    }

    [Fact]
    public void BuildLayout_LastTrack_DerivesLengthFromFileLengthProvider()
    {
        // Arrange
        // Single MODE1/2048 track of 100 cooked sectors -> file length = 100 * 2048 bytes.
        CueSheet sheet = SheetFromEntries(
            CreateEntry("solo.bin", CueFileType.Binary, TrackModeMode1Cooked, trackNumber: 1, indexNumber: 1, indexMsf: 150));
        CueFrameMapper mapper = new CueFrameMapper();
        long providedLength = 100L * 2048L;

        // Act
        IReadOnlyList<CueTrackLayout> layout = mapper.BuildLayout(sheet, _ => providedLength);

        // Assert
        layout.Should().HaveCount(1);
        layout[0].SectorSize.Should().Be(2048);
        layout[0].StartLba.Should().Be(0);
        layout[0].LengthSectors.Should().Be(100);
    }

    [Fact]
    public void BuildLayout_FileByteOffsetEqualsStartLbaTimesSectorSize()
    {
        // Arrange
        CueSheet sheet = SheetFromEntries(
            CreateEntry("disc.bin", CueFileType.Binary, TrackModeAudio, trackNumber: 1, indexNumber: 1, indexMsf: 150),
            CreateEntry("disc.bin", CueFileType.Binary, TrackModeAudio, trackNumber: 2, indexNumber: 1, indexMsf: 4500));
        CueFrameMapper mapper = new CueFrameMapper();

        // Act
        IReadOnlyList<CueTrackLayout> layout = mapper.BuildLayout(sheet, _ => 100_000_000L);

        // Assert
        layout[0].FileByteOffset.Should().Be(0L);
        layout[1].FileByteOffset.Should().Be(4350L * RawSectorSize);
    }

    [Fact]
    public void BuildLayout_PreservesPregapPostgapAndFileMetadata()
    {
        // Arrange
        CueEntry entry = CreateEntry("solo.bin", CueFileType.Binary, TrackModeAudio, trackNumber: 1, indexNumber: 1, indexMsf: 150);
        entry.Pregap = 75;
        entry.Postgap = 30;
        CueSheet sheet = SheetFromEntries(entry);
        CueFrameMapper mapper = new CueFrameMapper();

        // Act
        IReadOnlyList<CueTrackLayout> layout = mapper.BuildLayout(sheet, _ => 4L * RawSectorSize);

        // Assert
        layout[0].PregapFrames.Should().Be(75);
        layout[0].PostgapFrames.Should().Be(30);
        layout[0].FileName.Should().Be("solo.bin");
        layout[0].FileType.Should().Be(CueFileType.Binary);
        layout[0].TrackMode.Should().Be(TrackModeAudio);
    }

    [Fact]
    public void BuildLayout_NullSheet_Throws()
    {
        // Arrange
        CueFrameMapper mapper = new CueFrameMapper();

        // Act
        Action act = () => mapper.BuildLayout(null!, _ => 0L);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildLayout_NullFileLengthProvider_Throws()
    {
        // Arrange
        CueSheet sheet = SheetFromEntries(
            CreateEntry("a.bin", CueFileType.Binary, TrackModeAudio, trackNumber: 1, indexNumber: 1, indexMsf: 150));
        CueFrameMapper mapper = new CueFrameMapper();

        // Act
        Action act = () => mapper.BuildLayout(sheet, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static CueSheet SheetFromEntries(params CueEntry[] entries)
    {
        return new CueSheet(entries, catalog: null);
    }

    private static CueEntry CreateEntry(
        string fileName,
        CueFileType fileType,
        string trackMode,
        int trackNumber,
        int indexNumber,
        int indexMsf)
    {
        return new CueEntry
        {
            FileName = fileName,
            FileType = fileType,
            TrackMode = trackMode,
            TrackNumber = trackNumber,
            IndexNumber = indexNumber,
            IndexMsf = indexMsf,
        };
    }
}
