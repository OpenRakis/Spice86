using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using System.Buffers.Binary;

using Spice86.Shared.Emulator.Storage.CdRom.Mds;

namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>
/// Reads an Alcohol 120% MDS/MDF disc image and exposes it as an
/// <see cref="ICdRomImage"/>. Mirrors dosbox-staging's
/// <c>CDROM_Interface_Image::LoadMdsFile</c>: parses the MDS descriptor, opens
/// the referenced MDF data files, and exposes the resolved tracks plus a
/// synthetic lead-out entry at the end of the last track.
/// </summary>
public sealed class MdsImage : ICdRomImage
{
    private const int CookedSectorSize = 2048;
    private const int PvdLba = 16;

    private readonly List<CdTrack> _tracks = new List<CdTrack>();
    private readonly Dictionary<string, FileBackedDataSource> _sources = new Dictionary<string, FileBackedDataSource>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens and parses the MDS image at the given path.</summary>
    /// <param name="mdsFilePath">Path to the .mds file.</param>
    /// <exception cref="InvalidDataException">Thrown when the MDS file is malformed.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the MDF data file referenced by the descriptor cannot be opened.</exception>
    public MdsImage(string mdsFilePath)
    {
        ArgumentNullException.ThrowIfNull(mdsFilePath);
        ImagePath = mdsFilePath;

        MdsParser parser = new MdsParser();
        MdsDiscDescriptor descriptor = parser.ParseFile(mdsFilePath);

        string mdsDirectory = Path.GetDirectoryName(Path.GetFullPath(mdsFilePath)) ?? string.Empty;
        BuildTracks(descriptor, mdsDirectory, mdsFilePath);

        PrimaryVolume = ParsePrimaryVolume();
    }

    private void BuildTracks(MdsDiscDescriptor descriptor, string mdsDirectory, string mdsFilePath)
    {
        foreach (MdsTrack mdsTrack in descriptor.Tracks)
        {
            string mdfPath = ResolveMdfPath(mdsTrack.MdfFilename, mdsDirectory, mdsFilePath);
            FileBackedDataSource source = GetOrOpenSource(mdfPath);

            CdSectorMode mode = MapMode(mdsTrack);
            bool isAudio = mdsTrack.Mode == MdsTrackMode.Audio;
            CdTrack track = new CdTrack(
                number: mdsTrack.Number,
                startLba: mdsTrack.StartSector,
                lengthSectors: mdsTrack.LengthSectors,
                sectorSize: mdsTrack.SectorSize,
                mode: mode,
                isAudio: isAudio,
                pregap: 0,
                postgap: 0,
                source: source,
                fileOffset: mdsTrack.SkipBytes);
            _tracks.Add(track);
        }

        AppendLeadOut();
    }

    private void AppendLeadOut()
    {
        CdTrack last = _tracks[^1];
        int leadOutStart = last.StartLba + last.LengthSectors;
        CdTrack leadOut = new CdTrack(
            number: 0,
            startLba: leadOutStart,
            lengthSectors: 0,
            sectorSize: last.SectorSize,
            mode: last.Mode,
            isAudio: last.IsAudio,
            pregap: 0,
            postgap: 0,
            source: last.Source,
            fileOffset: last.FileOffset);
        _tracks.Add(leadOut);
    }

    private static string ResolveMdfPath(string mdfFilename, string mdsDirectory, string mdsFilePath)
    {
        if (string.Equals(mdfFilename, "*.mdf", StringComparison.OrdinalIgnoreCase))
        {
            string baseName = Path.GetFileNameWithoutExtension(mdsFilePath);
            return Path.Combine(mdsDirectory, baseName + ".mdf");
        }
        if (Path.IsPathRooted(mdfFilename))
        {
            return mdfFilename;
        }
        return Path.Combine(mdsDirectory, mdfFilename);
    }

    private FileBackedDataSource GetOrOpenSource(string mdfPath)
    {
        if (_sources.TryGetValue(mdfPath, out FileBackedDataSource? existing))
        {
            return existing;
        }
        FileBackedDataSource source = new FileBackedDataSource(mdfPath);
        _sources[mdfPath] = source;
        return source;
    }

    private static CdSectorMode MapMode(MdsTrack track)
    {
        if (track.Mode == MdsTrackMode.Audio)
        {
            return CdSectorMode.AudioRaw2352;
        }
        if (track.Mode == MdsTrackMode.Mode1Data)
        {
            return track.SectorSize == CookedSectorSize ? CdSectorMode.CookedData2048 : CdSectorMode.Raw2352;
        }
        return CdSectorMode.Mode2Form1;
    }

    private IsoVolumeDescriptor ParsePrimaryVolume()
    {
        CdTrack? dataTrack = FindFirstDataTrack();
        if (dataTrack == null)
        {
            return new IsoVolumeDescriptor(string.Empty, 0, 0, CookedSectorSize, TotalSectors);
        }

        byte[] sectorBuffer = new byte[dataTrack.SectorSize];
        long pvdByteOffset = dataTrack.FileOffset + (long)PvdLba * dataTrack.SectorSize;
        dataTrack.Source.Read(pvdByteOffset, sectorBuffer);

        ReadOnlySpan<byte> pvd = GetCookedData(sectorBuffer, dataTrack.Mode);
        if (pvd.Length < 190)
        {
            return new IsoVolumeDescriptor(string.Empty, 0, 0, CookedSectorSize, TotalSectors);
        }

        int volumeSpaceSize = BinaryPrimitives.ReadInt32LittleEndian(pvd.Slice(80, 4));
        int logicalBlockSize = BinaryPrimitives.ReadUInt16LittleEndian(pvd.Slice(128, 2));

        ReadOnlySpan<byte> rootDirSpan = pvd.Slice(156, 34);
        int rootDirLba = BinaryPrimitives.ReadInt32LittleEndian(rootDirSpan.Slice(2, 4));
        int rootDirSize = BinaryPrimitives.ReadInt32LittleEndian(rootDirSpan.Slice(10, 4));
        string volumeId = Encoding.ASCII.GetString(pvd.Slice(40, 32)).TrimEnd();

        return new IsoVolumeDescriptor(volumeId, rootDirLba, rootDirSize, logicalBlockSize, volumeSpaceSize);
    }

    private static ReadOnlySpan<byte> GetCookedData(byte[] rawSector, CdSectorMode mode)
    {
        if (mode == CdSectorMode.CookedData2048)
        {
            return rawSector;
        }
        if (mode == CdSectorMode.Raw2352)
        {
            return rawSector.AsSpan(16, CookedSectorSize);
        }
        if (mode == CdSectorMode.Mode2Form1)
        {
            return rawSector.AsSpan(24, CookedSectorSize);
        }
        return rawSector.AsSpan(0, Math.Min(rawSector.Length, CookedSectorSize));
    }

    private CdTrack? FindFirstDataTrack()
    {
        return _tracks.FirstOrDefault(t => !t.IsAudio && t.LengthSectors > 0);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CdTrack> Tracks => _tracks;

    /// <inheritdoc/>
    public int TotalSectors => _tracks.Sum(t => t.LengthSectors);

    /// <inheritdoc/>
    public IsoVolumeDescriptor PrimaryVolume { get; }

    /// <inheritdoc/>
    public string? UpcEan => null;

    /// <inheritdoc/>
    public string ImagePath { get; }

    /// <inheritdoc/>
    public int Read(int lba, Span<byte> destination, CdSectorMode mode)
    {
        CdTrack? track = FindTrack(lba);
        if (track == null || track.LengthSectors == 0)
        {
            return 0;
        }
        long fileByteOffset = track.FileOffset + (long)(lba - track.StartLba) * track.SectorSize;
        byte[] rawBuffer = new byte[track.SectorSize];
        track.Source.Read(fileByteOffset, rawBuffer);

        if (mode == CdSectorMode.CookedData2048 && track.Mode == CdSectorMode.Raw2352)
        {
            return SectorFraming.ExtractCookedFromRaw2352(rawBuffer, destination);
        }
        if (mode == CdSectorMode.CookedData2048 && track.Mode == CdSectorMode.Mode2Form1)
        {
            return SectorFraming.ExtractMode2Form1FromRaw2352(rawBuffer, destination);
        }
        rawBuffer.CopyTo(destination);
        return track.SectorSize;
    }

    private CdTrack? FindTrack(int lba)
    {
        return _tracks.FirstOrDefault(t => t.LengthSectors > 0 && lba >= t.StartLba && lba < t.StartLba + t.LengthSectors);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (FileBackedDataSource source in _sources.Values)
        {
            source.Dispose();
        }
        _sources.Clear();
    }
}
