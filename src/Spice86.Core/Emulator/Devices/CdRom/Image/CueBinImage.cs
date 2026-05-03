using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Reads a CUE/BIN disc image and exposes it as an <see cref="ICdRomImage"/>.</summary>
public sealed class CueBinImage : ICdRomImage {
    private const int CookedSectorSize = 2048;
    private const int PvdLba = 16;

    private readonly List<CdTrack> _tracks = new List<CdTrack>();
    private readonly Dictionary<string, FileBackedDataSource> _sources = new Dictionary<string, FileBackedDataSource>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens and parses a CUE/BIN disc image.</summary>
    /// <param name="cueFilePath">Path to the .cue file.</param>
    /// <exception cref="IOException">Thrown when a BIN file cannot be opened.</exception>
    /// <exception cref="InvalidDataException">Thrown when the CUE sheet is malformed.</exception>
    public CueBinImage(string cueFilePath) {
        ImagePath = cueFilePath;
        CueSheetParser parser = new CueSheetParser();
        CueSheet sheet = parser.Parse(cueFilePath);
        UpcEan = sheet.Catalog;

        BuildTracks(sheet);
        PrimaryVolume = ParsePrimaryVolume();
    }

    private void BuildTracks(CueSheet sheet) {
        // Group INDEX entries by track number.
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

        // First pass: collect per-track index-01 frames and metadata.
        List<(int TrackNum, int Index01Frames, int Pregap, int Postgap, string FileName, string TrackMode)> trackMeta =
            new List<(int, int, int, int, string, string)>();

        foreach (int trackNum in trackNumbers) {
            List<CueEntry> entries = byTrack[trackNum];
            int index01Frames = 0;
            int pregap = 0;
            int postgap = 0;
            string fileName = string.Empty;
            string trackMode = string.Empty;

            foreach (CueEntry e in entries) {
                fileName = e.FileName;
                trackMode = e.TrackMode;
                pregap = e.Pregap;
                postgap = e.Postgap;
                if (e.IndexNumber == 1) {
                    index01Frames = e.IndexMsf;
                }
            }

            trackMeta.Add((trackNum, index01Frames, pregap, postgap, fileName, trackMode));
        }

        // Second pass: build CdTrack objects with lengths derived from next track's start.
        for (int i = 0; i < trackMeta.Count; i++) {
            (int trackNum, int index01Frames, int pregap, int postgap, string fileName, string trackMode) = trackMeta[i];

            int startLba = index01Frames;
            int nextStartLba;
            if (i + 1 < trackMeta.Count) {
                nextStartLba = trackMeta[i + 1].Item2;
            } else {
                FileBackedDataSource measureSource = OpenSource(fileName);
                int sizeForMeasure = MapSectorSize(trackMode);
                nextStartLba = startLba + (int)(measureSource.LengthBytes / sizeForMeasure) - startLba;
                // Use total file sectors as the length for the last track.
                nextStartLba = (int)(measureSource.LengthBytes / sizeForMeasure);
            }

            int lengthSectors = nextStartLba - startLba;
            if (lengthSectors <= 0) {
                lengthSectors = 1;
            }

            int sectorSize = MapSectorSize(trackMode);
            CdSectorMode mode = MapMode(trackMode);
            bool isAudio = IsAudioMode(trackMode);

            FileBackedDataSource source = OpenSource(fileName);
            long fileOffset = (long)startLba * sectorSize;

            CdTrack track = new CdTrack(
                number: trackNum,
                startLba: startLba,
                lengthSectors: lengthSectors,
                sectorSize: sectorSize,
                mode: mode,
                isAudio: isAudio,
                pregap: pregap,
                postgap: postgap,
                source: source,
                fileOffset: fileOffset);
            _tracks.Add(track);
        }
    }

    private FileBackedDataSource OpenSource(string filePath) {
        if (_sources.TryGetValue(filePath, out FileBackedDataSource? existing)) {
            return existing;
        }
        FileBackedDataSource source = new FileBackedDataSource(filePath);
        _sources[filePath] = source;
        return source;
    }

    private static int MapSectorSize(string trackMode) {
        if (trackMode.Equals("MODE1/2048", StringComparison.OrdinalIgnoreCase)) {
            return 2048;
        }
        if (trackMode.Equals("MODE1/2352", StringComparison.OrdinalIgnoreCase)) {
            return 2352;
        }
        if (trackMode.Equals("MODE2/2336", StringComparison.OrdinalIgnoreCase)) {
            return 2336;
        }
        if (trackMode.Equals("MODE2/2352", StringComparison.OrdinalIgnoreCase)) {
            return 2352;
        }
        if (trackMode.Equals("AUDIO", StringComparison.OrdinalIgnoreCase)) {
            return 2352;
        }
        return 2352;
    }

    private static CdSectorMode MapMode(string trackMode) {
        if (trackMode.Equals("MODE1/2048", StringComparison.OrdinalIgnoreCase)) {
            return CdSectorMode.CookedData2048;
        }
        if (trackMode.Equals("MODE1/2352", StringComparison.OrdinalIgnoreCase)) {
            return CdSectorMode.Raw2352;
        }
        if (trackMode.Equals("MODE2/2336", StringComparison.OrdinalIgnoreCase)) {
            return CdSectorMode.Mode2Form2;
        }
        if (trackMode.Equals("MODE2/2352", StringComparison.OrdinalIgnoreCase)) {
            return CdSectorMode.Mode2Form1;
        }
        if (trackMode.Equals("AUDIO", StringComparison.OrdinalIgnoreCase)) {
            return CdSectorMode.AudioRaw2352;
        }
        return CdSectorMode.Raw2352;
    }

    private static bool IsAudioMode(string trackMode) {
        return trackMode.Equals("AUDIO", StringComparison.OrdinalIgnoreCase);
    }

    private IsoVolumeDescriptor ParsePrimaryVolume() {
        CdTrack? dataTrack = FindFirstDataTrack();
        if (dataTrack == null) {
            return new IsoVolumeDescriptor(string.Empty, 0, 0, CookedSectorSize, TotalSectors);
        }

        byte[] sectorBuffer = new byte[dataTrack.SectorSize];
        long pvdByteOffset = dataTrack.FileOffset + (long)PvdLba * dataTrack.SectorSize;
        dataTrack.Source.Read(pvdByteOffset, sectorBuffer);

        ReadOnlySpan<byte> pvd = GetCookedData(sectorBuffer, dataTrack.Mode);

        int volumeSpaceSize = BinaryPrimitives.ReadInt32LittleEndian(pvd.Slice(80, 4));
        int logicalBlockSize = BinaryPrimitives.ReadUInt16LittleEndian(pvd.Slice(128, 2));

        ReadOnlySpan<byte> rootDirSpan = pvd.Slice(156, 34);
        int rootDirLba = BinaryPrimitives.ReadInt32LittleEndian(rootDirSpan.Slice(2, 4));
        int rootDirSize = BinaryPrimitives.ReadInt32LittleEndian(rootDirSpan.Slice(10, 4));
        string volumeId = Encoding.ASCII.GetString(pvd.Slice(40, 32)).TrimEnd();

        return new IsoVolumeDescriptor(volumeId, rootDirLba, rootDirSize, logicalBlockSize, volumeSpaceSize);
    }

    private static ReadOnlySpan<byte> GetCookedData(byte[] rawSector, CdSectorMode mode) {
        if (mode == CdSectorMode.CookedData2048) {
            return rawSector;
        }
        if (mode == CdSectorMode.Raw2352) {
            return rawSector.AsSpan(16, 2048);
        }
        if (mode == CdSectorMode.Mode2Form1) {
            return rawSector.AsSpan(24, 2048);
        }
        return rawSector.AsSpan(0, CookedSectorSize);
    }

    private CdTrack? FindFirstDataTrack() {
        foreach (CdTrack t in _tracks) {
            if (!t.IsAudio) {
                return t;
            }
        }
        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CdTrack> Tracks => _tracks;

    /// <inheritdoc/>
    public int TotalSectors {
        get {
            int total = 0;
            foreach (CdTrack t in _tracks) {
                total += t.LengthSectors;
            }
            return total;
        }
    }

    /// <inheritdoc/>
    public IsoVolumeDescriptor PrimaryVolume { get; }

    /// <inheritdoc/>
    public string? UpcEan { get; }

    /// <inheritdoc/>
    public string ImagePath { get; }

    /// <inheritdoc/>
    public int Read(int lba, Span<byte> destination, CdSectorMode mode) {
        CdTrack? track = FindTrack(lba);
        if (track == null) {
            return 0;
        }

        long fileByteOffset = track.FileOffset + (long)(lba - track.StartLba) * track.SectorSize;
        byte[] rawBuffer = new byte[track.SectorSize];
        track.Source.Read(fileByteOffset, rawBuffer);

        if (mode == CdSectorMode.CookedData2048 && track.Mode == CdSectorMode.Raw2352) {
            return SectorFraming.ExtractCookedFromRaw2352(rawBuffer, destination);
        }
        if (mode == CdSectorMode.CookedData2048 && track.Mode == CdSectorMode.Mode2Form1) {
            return SectorFraming.ExtractMode2Form1FromRaw2352(rawBuffer, destination);
        }
        rawBuffer.CopyTo(destination);
        return track.SectorSize;
    }

    private CdTrack? FindTrack(int lba) {
        foreach (CdTrack t in _tracks) {
            if (lba >= t.StartLba && lba < t.StartLba + t.LengthSectors) {
                return t;
            }
        }
        return null;
    }

    /// <inheritdoc/>
    public void Dispose() {
        foreach (FileBackedDataSource source in _sources.Values) {
            source.Dispose();
        }
        _sources.Clear();
    }
}
