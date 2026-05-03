namespace Spice86.Core.Emulator.OperatingSystem.FileSystem;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Provides read-only access to a FAT12 filesystem stored in a floppy disk image (raw sector dump).
/// </summary>
/// <remarks>
/// Only FAT12 is supported. FAT12 is used exclusively on floppy disks (up to ~32 MB,
/// though in practice always well below 2 MB for standard floppy sizes).
/// </remarks>
public sealed class Fat12FileSystem {
    private const ushort EndOfChainMin = 0xFF8;
    private const ushort BadCluster = 0xFF7;
    private const int FatEntryBitsPerEntry = 12;

    private readonly byte[] _imageData;
    private readonly BiosParameterBlock _bpb;
    private readonly ushort[] _fat;

    /// <summary>Gets the volume label found in the BPB or the root-directory volume-label entry.</summary>
    public string VolumeLabel { get; }

    /// <summary>Gets the BIOS Parameter Block parsed from the boot sector.</summary>
    public BiosParameterBlock Bpb => _bpb;

    /// <summary>
    /// Opens a FAT12 floppy image from raw bytes.
    /// </summary>
    /// <param name="imageData">The full contents of the floppy image file.</param>
    /// <exception cref="InvalidDataException">Thrown when the image is not a valid FAT12 volume.</exception>
    public Fat12FileSystem(byte[] imageData) {
        _imageData = imageData;
        _bpb = BiosParameterBlock.Parse(_imageData.AsSpan(0, Math.Min(512, _imageData.Length)));
        _fat = ReadFat();
        VolumeLabel = FindVolumeLabel();
    }

    /// <summary>
    /// Opens a FAT12 floppy image from a file on disk.
    /// </summary>
    /// <param name="imagePath">Path to the .img file.</param>
    /// <returns>A new <see cref="Fat12FileSystem"/> backed by the file data.</returns>
    public static Fat12FileSystem FromFile(string imagePath) {
        return new Fat12FileSystem(File.ReadAllBytes(imagePath));
    }

    /// <summary>
    /// Lists all non-deleted, non-LFN entries in the root directory.
    /// </summary>
    public IReadOnlyList<FatDirectoryEntry> ListRootDirectory() {
        return ReadDirectory(_bpb.RootDirStartSector, isRootDir: true);
    }

    /// <summary>
    /// Lists all non-deleted, non-LFN entries in a subdirectory identified by its first cluster.
    /// </summary>
    /// <param name="firstCluster">The first cluster of the subdirectory.</param>
    public IReadOnlyList<FatDirectoryEntry> ListSubDirectory(ushort firstCluster) {
        return ReadDirectory(ClusterToSector(firstCluster), isRootDir: false);
    }

    /// <summary>
    /// Reads the contents of a file by following its cluster chain.
    /// </summary>
    /// <param name="entry">The directory entry for the file.</param>
    /// <returns>The raw bytes of the file, trimmed to <see cref="FatDirectoryEntry.FileSize"/>.</returns>
    public byte[] ReadFile(FatDirectoryEntry entry) {
        if (entry.IsDirectory) {
            throw new InvalidOperationException("Cannot read a directory as a file.");
        }

        IReadOnlyList<ushort> chain = GetClusterChain(entry.FirstCluster);
        byte[] allBytes = ReadClusters(chain);

        // Trim to actual file size.
        int size = (int)entry.FileSize;
        if (size > allBytes.Length) {
            size = allBytes.Length;
        }
        if (size == allBytes.Length) {
            return allBytes;
        }
        return allBytes[..size];
    }

    /// <summary>
    /// Resolves a DOS path (e.g. <c>"DIR\FILE.TXT"</c>) within the FAT12 volume and returns the matching entry.
    /// Path components are separated by <c>'\'</c> or <c>'/'</c>.
    /// </summary>
    /// <param name="dosPath">Case-insensitive DOS path relative to the drive root.</param>
    /// <param name="entry">The matching entry when found.</param>
    /// <returns><see langword="true"/> if the entry was found; otherwise <see langword="false"/>.</returns>
    public bool TryGetEntry(string dosPath, out FatDirectoryEntry? entry) {
        string[] parts = dosPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<FatDirectoryEntry> currentDir = ListRootDirectory();

        for (int i = 0; i < parts.Length; i++) {
            FatDirectoryEntry? found = currentDir.FirstOrDefault(e =>
                string.Equals(e.DosName, parts[i], StringComparison.OrdinalIgnoreCase));

            if (found == null) {
                entry = null;
                return false;
            }

            if (i == parts.Length - 1) {
                entry = found;
                return true;
            }

            if (!found.IsDirectory) {
                entry = null;
                return false;
            }

            currentDir = ListSubDirectory(found.FirstCluster);
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if a file or directory exists at <paramref name="dosPath"/>.
    /// </summary>
    public bool Exists(string dosPath) {
        return TryGetEntry(dosPath, out _);
    }

    // ---------- internals ----------

    private ushort[] ReadFat() {
        int fatByteOffset = _bpb.FatStartSector * _bpb.BytesPerSector;
        int fatByteLength = _bpb.SectorsPerFat * _bpb.BytesPerSector;

        // Total FAT12 entries = number of clusters + 2 (cluster 0 and 1 are reserved).
        int dataSectors = _bpb.TotalSectors - _bpb.DataStartSector;
        int clusterCount = dataSectors / _bpb.SectorsPerCluster;
        int entryCount = clusterCount + 2;

        ushort[] table = new ushort[entryCount];

        for (int n = 0; n < entryCount; n++) {
            int byteIndex = fatByteOffset + (n * FatEntryBitsPerEntry / 8);
            if (byteIndex + 1 >= fatByteOffset + fatByteLength) {
                break;
            }

            int rawWord = _imageData[byteIndex] | (_imageData[byteIndex + 1] << 8);
            if ((n & 1) == 0) {
                table[n] = (ushort)(rawWord & 0x0FFF);
            } else {
                table[n] = (ushort)((rawWord >> 4) & 0x0FFF);
            }
        }

        return table;
    }

    private IReadOnlyList<ushort> GetClusterChain(ushort startCluster) {
        List<ushort> chain = new();
        ushort current = startCluster;

        while (current >= 2 && current < EndOfChainMin && current != BadCluster) {
            chain.Add(current);
            if (current >= _fat.Length) {
                break;
            }
            current = _fat[current];
        }

        return chain;
    }

    private byte[] ReadClusters(IReadOnlyList<ushort> chain) {
        int totalBytes = chain.Count * _bpb.BytesPerCluster;
        byte[] result = new byte[totalBytes];
        int offset = 0;

        foreach (ushort cluster in chain) {
            int sectorStart = ClusterToSector(cluster);
            int byteOffset = sectorStart * _bpb.BytesPerSector;
            int bytesToCopy = _bpb.BytesPerCluster;

            if (byteOffset + bytesToCopy > _imageData.Length) {
                bytesToCopy = Math.Max(0, _imageData.Length - byteOffset);
            }

            Array.Copy(_imageData, byteOffset, result, offset, bytesToCopy);
            offset += bytesToCopy;
        }

        return result;
    }

    private int ClusterToSector(ushort cluster) {
        return _bpb.DataStartSector + (cluster - 2) * _bpb.SectorsPerCluster;
    }

    private IReadOnlyList<FatDirectoryEntry> ReadDirectory(int startSector, bool isRootDir) {
        List<FatDirectoryEntry> entries = new();

        if (isRootDir) {
            ReadDirectoryFromSectors(startSector, _bpb.RootDirEntries, entries);
        } else {
            // Subdirectory: follow cluster chain.
            ushort cluster = (ushort)(((startSector - _bpb.DataStartSector) / _bpb.SectorsPerCluster) + 2);
            ReadDirectoryFromClusters(cluster, entries);
        }

        return entries;
    }

    private void ReadDirectoryFromSectors(int startSector, int maxEntries, List<FatDirectoryEntry> entries) {
        int byteOffset = startSector * _bpb.BytesPerSector;

        for (int i = 0; i < maxEntries; i++) {
            int entryOffset = byteOffset + i * FatDirectoryEntry.EntrySize;
            if (entryOffset + FatDirectoryEntry.EntrySize > _imageData.Length) {
                break;
            }

            FatDirectoryEntry entry = FatDirectoryEntry.Parse(_imageData.AsSpan(entryOffset, FatDirectoryEntry.EntrySize));

            if (entry.IsEndMarker) {
                break;
            }
            if (entry.IsDeleted || entry.IsLfn) {
                continue;
            }
            entries.Add(entry);
        }
    }

    private void ReadDirectoryFromClusters(ushort startCluster, List<FatDirectoryEntry> entries) {
        IReadOnlyList<ushort> chain = GetClusterChain(startCluster);
        byte[] clusterData = ReadClusters(chain);
        int entriesInCluster = clusterData.Length / FatDirectoryEntry.EntrySize;

        for (int i = 0; i < entriesInCluster; i++) {
            int entryOffset = i * FatDirectoryEntry.EntrySize;
            FatDirectoryEntry entry = FatDirectoryEntry.Parse(clusterData.AsSpan(entryOffset, FatDirectoryEntry.EntrySize));

            if (entry.IsEndMarker) {
                break;
            }
            if (entry.IsDeleted || entry.IsLfn) {
                continue;
            }
            entries.Add(entry);
        }
    }

    private string FindVolumeLabel() {
        // Prefer the BPB label.
        if (!string.IsNullOrWhiteSpace(_bpb.VolumeLabel) &&
            !string.Equals(_bpb.VolumeLabel, "NO NAME", StringComparison.OrdinalIgnoreCase)) {
            return _bpb.VolumeLabel;
        }

        // Fall back to root directory volume-label entry.
        int rootDirByteOffset = _bpb.RootDirStartSector * _bpb.BytesPerSector;
        for (int i = 0; i < _bpb.RootDirEntries; i++) {
            int offset = rootDirByteOffset + i * FatDirectoryEntry.EntrySize;
            if (offset + FatDirectoryEntry.EntrySize > _imageData.Length) {
                break;
            }

            FatDirectoryEntry entry = FatDirectoryEntry.Parse(_imageData.AsSpan(offset, FatDirectoryEntry.EntrySize));
            if (entry.IsEndMarker) {
                break;
            }
            if (entry.IsVolumeLabel && !entry.IsDeleted) {
                return (entry.BaseName + entry.Extension).TrimEnd();
            }
        }

        return string.Empty;
    }
}
