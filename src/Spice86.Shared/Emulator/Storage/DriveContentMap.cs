namespace Spice86.Shared.Emulator.Storage;

using System.Collections.Generic;

/// <summary>
/// Snapshot describing the on-disk layout of a mounted drive, suitable for UI visualisation.
/// Either <see cref="Tracks"/> (for CD-ROM drives) or <see cref="Clusters"/> (for FAT drives) is populated.
/// </summary>
public sealed class DriveContentMap {
    /// <summary>
    /// Initialises a new <see cref="DriveContentMap"/> for a CD-ROM drive.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter (uppercase).</param>
    /// <param name="totalSectors">The total number of sectors on the medium.</param>
    /// <param name="tracks">The tracks defined on the medium.</param>
    /// <returns>A populated <see cref="DriveContentMap"/>.</returns>
    public static DriveContentMap ForCdRom(char driveLetter, uint totalSectors, IReadOnlyList<DriveCdTrackInfo> tracks) {
        return new DriveContentMap(driveLetter, totalSectors, tracks, null, 0);
    }

    /// <summary>
    /// Initialises a new <see cref="DriveContentMap"/> for a FAT-formatted drive.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter (uppercase).</param>
    /// <param name="clusters">The cluster bitmap (capped to a reasonable display size).</param>
    /// <param name="totalClusters">The actual total number of clusters in the file system.</param>
    /// <returns>A populated <see cref="DriveContentMap"/>.</returns>
    public static DriveContentMap ForFat(char driveLetter, IReadOnlyList<DriveClusterInfo> clusters, int totalClusters) {
        return new DriveContentMap(driveLetter, 0, null, clusters, totalClusters);
    }

    private DriveContentMap(char driveLetter, uint totalSectors, IReadOnlyList<DriveCdTrackInfo>? tracks,
        IReadOnlyList<DriveClusterInfo>? clusters, int totalClusters) {
        DriveLetter = driveLetter;
        TotalSectors = totalSectors;
        Tracks = tracks;
        Clusters = clusters;
        TotalClusters = totalClusters;
    }

    /// <summary>Gets the uppercase DOS drive letter.</summary>
    public char DriveLetter { get; }

    /// <summary>Gets the total CD-ROM sector count, or zero for FAT drives.</summary>
    public uint TotalSectors { get; }

    /// <summary>Gets the CD-ROM track list, or <see langword="null"/> for FAT drives.</summary>
    public IReadOnlyList<DriveCdTrackInfo>? Tracks { get; }

    /// <summary>Gets the FAT cluster bitmap, or <see langword="null"/> for CD-ROM drives.</summary>
    public IReadOnlyList<DriveClusterInfo>? Clusters { get; }

    /// <summary>Gets the actual total cluster count of the FAT file system (may exceed <see cref="Clusters"/> count when truncated).</summary>
    public int TotalClusters { get; }

    /// <summary>Gets a value indicating whether this map describes a CD-ROM.</summary>
    public bool IsCdRom => Tracks != null;

    /// <summary>Gets a value indicating whether this map describes a FAT volume.</summary>
    public bool IsFat => Clusters != null;
}
