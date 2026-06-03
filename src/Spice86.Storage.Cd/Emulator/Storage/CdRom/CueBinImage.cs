using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using System.Buffers.Binary;

using Spice86.Shared.Emulator.Storage.CdRom.Audio;

namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>Reads a CUE/BIN disc image and exposes it as an <see cref="ICdRomImage"/>.</summary>
public sealed class CueBinImage : ICdRomImage {
    private const int CookedSectorSize = 2048;
    private const int PvdLba = 16;

    private readonly List<CdTrack> _tracks = new List<CdTrack>();
    private readonly Dictionary<string, IDataSource> _sources = new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _ownedDisposables = new List<IDisposable>();
    private readonly CompositeAudioCodecFactory _codecFactory;

    /// <summary>Opens and parses a CUE/BIN disc image using the default audio codec factory.</summary>
    /// <param name="cueFilePath">Path to the .cue file.</param>
    /// <exception cref="IOException">Thrown when a BIN file cannot be opened.</exception>
    /// <exception cref="InvalidDataException">Thrown when the CUE sheet is malformed.</exception>
    public CueBinImage(string cueFilePath)
        : this(cueFilePath, DefaultAudioCodecFactory.Create()) {
    }

    /// <summary>Opens and parses a CUE/BIN disc image using the supplied audio codec factory.</summary>
    /// <param name="cueFilePath">Path to the .cue file.</param>
    /// <param name="codecFactory">Composite factory used to decode non-BINARY audio file references.</param>
    /// <exception cref="IOException">Thrown when a BIN file cannot be opened.</exception>
    /// <exception cref="InvalidDataException">Thrown when the CUE sheet is malformed.</exception>
    public CueBinImage(string cueFilePath, CompositeAudioCodecFactory codecFactory)
        : this(CueSheetParser.Parse(cueFilePath), cueFilePath, codecFactory) {
    }

    /// <summary>
    /// Constructs a CUE/BIN image from a pre-built <see cref="CueSheet"/> (e.g. synthesized for a raw
    /// .bin file that has no companion .cue), using the default audio codec factory.
    /// </summary>
    /// <param name="sheet">In-memory CUE sheet with absolute file paths.</param>
    /// <param name="imagePath">Path used to identify this image (typically the .bin path).</param>
    public CueBinImage(CueSheet sheet, string imagePath)
        : this(sheet, imagePath, DefaultAudioCodecFactory.Create()) {
    }

    /// <summary>
    /// Core constructor: builds the image from an already-parsed <see cref="CueSheet"/>.
    /// All other constructors funnel through here.
    /// </summary>
    /// <param name="sheet">CUE sheet (parsed from disk or synthesized in memory).</param>
    /// <param name="imagePath">Path used to identify this image (the .cue or the raw .bin path).</param>
    /// <param name="codecFactory">Composite factory used to decode non-BINARY audio file references.</param>
    public CueBinImage(CueSheet sheet, string imagePath, CompositeAudioCodecFactory codecFactory) {
        if (sheet == null) {
            throw new ArgumentNullException(nameof(sheet));
        }
        if (codecFactory == null) {
            throw new ArgumentNullException(nameof(codecFactory));
        }
        _codecFactory = codecFactory;
        ImagePath = imagePath;
        UpcEan = sheet.Catalog;

        BuildTracks(sheet);
        PrimaryVolume = ParsePrimaryVolume();
    }

    private void BuildTracks(CueSheet sheet) {
        CueFrameMapper mapper = new CueFrameMapper();
        IReadOnlyList<CueTrackLayout> layouts = CueFrameMapper.BuildLayout(sheet, GetFileLengthBytes);

        foreach (CueTrackLayout layout in layouts) {
            CdSectorMode mode = MapMode(layout.TrackMode);
            bool isAudio = IsAudioMode(layout.TrackMode);
            IDataSource source = OpenSource(layout.FileName, layout.FileType);

            CdTrack track = new CdTrack(
                number: layout.TrackNumber,
                startLba: layout.StartLba,
                lengthSectors: layout.LengthSectors,
                sectorSize: layout.SectorSize,
                mode: mode,
                isAudio: isAudio,
                pregap: layout.PregapFrames,
                postgap: layout.PostgapFrames,
                source: source,
                fileOffset: layout.FileByteOffset);
            _tracks.Add(track);
        }
    }

    private long GetFileLengthBytes(string filePath) {
        // Reuse already-opened sources so we don't double-decode audio codecs.
        if (_sources.TryGetValue(filePath, out IDataSource? existing)) {
            return existing.LengthBytes;
        }
        // For BINARY files we can avoid the codec dispatch entirely.
        if (System.IO.File.Exists(filePath)) {
            System.IO.FileInfo info = new System.IO.FileInfo(filePath);
            return info.Length;
        }
        return 0L;
    }

    private IDataSource OpenSource(string filePath, CueFileType fileType) {
        if (_sources.TryGetValue(filePath, out IDataSource? existing)) {
            return existing;
        }
        IDataSource source = CreateSource(filePath, fileType);
        _sources[filePath] = source;
        return source;
    }

    private IDataSource CreateSource(string filePath, CueFileType fileType) {
        if (fileType == CueFileType.Binary) {
            FileBackedDataSource fileSource = new FileBackedDataSource(filePath);
            _ownedDisposables.Add(fileSource);
            return fileSource;
        }
        IAudioCodec codec = _codecFactory.CreateFor(fileType, filePath);
        if (codec is IDisposable disposableCodec) {
            _ownedDisposables.Add(disposableCodec);
        }
        return codec.OpenAsCdda(filePath);
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
        return _tracks.FirstOrDefault(t => !t.IsAudio);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CdTrack> Tracks => _tracks;

    /// <inheritdoc/>
    public int TotalSectors => _tracks.Sum(t => t.LengthSectors);

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
        int bytesRead = track.Source.Read(fileByteOffset, rawBuffer);
        if (bytesRead != track.SectorSize) {
            return 0;
        }

        return SectorFraming.CopySector(rawBuffer, destination, track.Mode, mode);
    }

    private CdTrack? FindTrack(int lba) {
        return _tracks.FirstOrDefault(t => lba >= t.StartLba && lba < t.StartLba + t.LengthSectors);
    }

    /// <inheritdoc/>
    public void Dispose() {
        foreach (IDisposable disposable in _ownedDisposables) {
            disposable.Dispose();
        }
        _ownedDisposables.Clear();
        _sources.Clear();
    }
}
