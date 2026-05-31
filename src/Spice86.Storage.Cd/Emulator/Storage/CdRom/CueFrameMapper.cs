using System.Collections.Generic;
using System.Linq;

namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>
/// Converts a parsed <see cref="CueSheet"/> into a list of resolved
/// <see cref="CueTrackLayout"/> entries, applying Red Book frame padding,
/// INDEX 00 (in-file pregap) accounting, and last-track length derivation
/// from the backing file size. Mirrors DOSBox Staging's <c>AddTrack</c>
/// "skip" semantics so that the previous track's TOC length terminates at
/// the next track's INDEX 00 when one is declared.
/// </summary>
public sealed class CueFrameMapper {
    /// <summary>
    /// Red Book pre-gap offset in frames (2 seconds * 75 frames/second).
    /// Matches DOSBox Staging's <c>REDBOOK_FRAME_PADDING = 150</c>.
    /// </summary>
    public const int RedbookPreGapFrames = 150;

    /// <summary>
    /// Builds a sector-accurate layout for every track in <paramref name="sheet"/>.
    /// </summary>
    /// <param name="sheet">Parsed CUE sheet whose entries will be grouped per track.</param>
    /// <param name="fileLengthProvider">
    /// Delegate returning the byte length of a given file path. Invoked only for
    /// tracks whose length cannot be inferred from a following track in the same
    /// file (typically the last track).
    /// </param>
    /// <returns>One <see cref="CueTrackLayout"/> per declared track, in CUE order.</returns>
    public IReadOnlyList<CueTrackLayout> BuildLayout(CueSheet sheet, Func<string, long> fileLengthProvider) {
        if (sheet == null) {
            throw new ArgumentNullException(nameof(sheet));
        }
        if (fileLengthProvider == null) {
            throw new ArgumentNullException(nameof(fileLengthProvider));
        }

        List<RawTrack> raws = CollectRawTracks(sheet);
        List<CueTrackLayout> resolved = new List<CueTrackLayout>(raws.Count);

        for (int i = 0; i < raws.Count; i++) {
            RawTrack current = raws[i];
            int sectorSize = TrackModeSectorSize(current.TrackMode);
            int startLba = Math.Max(0, current.Index01Frames - RedbookPreGapFrames);
            int lengthSectors;

            if (i + 1 < raws.Count) {
                RawTrack next = raws[i + 1];
                int nextStartLba = Math.Max(0, next.Index01Frames - RedbookPreGapFrames);
                int skip = 0;
                if (next.Index00Frames.HasValue) {
                    skip = next.Index01Frames - next.Index00Frames.Value;
                }
                lengthSectors = nextStartLba - startLba - skip;
                if (lengthSectors <= 0) {
                    lengthSectors = 1;
                }
            } else {
                long fileLength = fileLengthProvider(current.FileName);
                int totalSectors = (int)(fileLength / sectorSize);
                lengthSectors = totalSectors - startLba;
                if (lengthSectors <= 0) {
                    lengthSectors = 1;
                }
            }

            long fileByteOffset = (long)startLba * sectorSize;

            CueTrackLayout layout = new CueTrackLayout(
                trackNumber: current.TrackNumber,
                fileName: current.FileName,
                fileType: current.FileType,
                trackMode: current.TrackMode,
                index01Frames: current.Index01Frames,
                index00Frames: current.Index00Frames,
                startLba: startLba,
                lengthSectors: lengthSectors,
                fileByteOffset: fileByteOffset,
                sectorSize: sectorSize,
                pregapFrames: current.Pregap,
                postgapFrames: current.Postgap);
            resolved.Add(layout);
        }

        return resolved;
    }

    private static List<RawTrack> CollectRawTracks(CueSheet sheet) {
        Dictionary<int, List<CueEntry>> byTrack = new Dictionary<int, List<CueEntry>>();
        foreach (CueEntry entry in sheet.Entries) {
            if (!byTrack.TryGetValue(entry.TrackNumber, out List<CueEntry>? bucket)) {
                bucket = new List<CueEntry>();
                byTrack[entry.TrackNumber] = bucket;
            }
            bucket.Add(entry);
        }

        List<int> trackNumbers = new List<int>(byTrack.Keys);
        trackNumbers.Sort();

        List<RawTrack> raws = new List<RawTrack>(trackNumbers.Count);
        foreach (int trackNum in trackNumbers) {
            List<CueEntry> entries = byTrack[trackNum];
            if (entries.Count == 0) {
                continue;
            }
            CueEntry first = entries[0];
            CueEntry? idx01 = entries.FirstOrDefault(e => e.IndexNumber == 1);
            CueEntry? idx00 = entries.FirstOrDefault(e => e.IndexNumber == 0);
            int index01 = idx01 != null ? idx01.IndexMsf : 0;
            int? index00 = idx00 != null ? idx00.IndexMsf : (int?)null;

            raws.Add(new RawTrack(
                trackNum,
                first.FileName,
                first.FileType,
                first.TrackMode,
                index01,
                index00,
                first.Pregap,
                first.Postgap));
        }

        return raws;
    }

    private static int TrackModeSectorSize(string trackMode) {
        if (trackMode.Equals("MODE1/2048", StringComparison.OrdinalIgnoreCase)) {
            return 2048;
        }
        if (trackMode.Equals("MODE2/2336", StringComparison.OrdinalIgnoreCase)) {
            return 2336;
        }
        return 2352;
    }

    private sealed class RawTrack {
        public int TrackNumber { get; }
        public string FileName { get; }
        public CueFileType FileType { get; }
        public string TrackMode { get; }
        public int Index01Frames { get; }
        public int? Index00Frames { get; }
        public int Pregap { get; }
        public int Postgap { get; }

        public RawTrack(
            int trackNumber,
            string fileName,
            CueFileType fileType,
            string trackMode,
            int index01Frames,
            int? index00Frames,
            int pregap,
            int postgap) {
            TrackNumber = trackNumber;
            FileName = fileName;
            FileType = fileType;
            TrackMode = trackMode;
            Index01Frames = index01Frames;
            Index00Frames = index00Frames;
            Pregap = pregap;
            Postgap = postgap;
        }
    }
}
