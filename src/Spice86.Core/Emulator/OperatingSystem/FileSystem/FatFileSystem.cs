namespace Spice86.Core.Emulator.OperatingSystem.FileSystem;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Provides read-only access to a FAT12, FAT16, or FAT32 filesystem stored in a disk image.
/// </summary>
public sealed class FatFileSystem {
    private const uint Fat12EndOfChainMin = 0xFF8;
    private const uint Fat12BadCluster = 0xFF7;
    private const uint Fat16EndOfChainMin = 0xFFF8;
    private const uint Fat16BadCluster = 0xFFF7;
    private const uint Fat32EndOfChainMin = 0x0FFFFFF8;
    private const uint Fat32BadCluster = 0x0FFFFFF7;

    private readonly byte[] _imageData;
    private readonly BiosParameterBlock _bpb;
    private readonly uint[] _fat;

    /// <summary>Gets the type of FAT used in this volume.</summary>
    public FatType FatType { get; }

    /// <summary>Gets the volume label.</summary>
    public string VolumeLabel { get; }

    /// <summary>Gets the BIOS Parameter Block parsed from the boot sector.</summary>
    public BiosParameterBlock Bpb => _bpb;

    /// <summary>
    /// Opens a FAT filesystem image from raw bytes.
    /// </summary>
    /// <param name="imageData">The full contents of the disk image file.</param>
    /// <exception cref="InvalidDataException">Thrown when the image is not a valid FAT volume.</exception>
    public FatFileSystem(byte[] imageData) {
        _imageData = imageData;
        _bpb = BiosParameterBlock.Parse(_imageData.AsSpan(0, Math.Min(512, _imageData.Length)));
        FatType = DetectFatType();
        _fat = ReadFat();
        VolumeLabel = FindVolumeLabel();
    }

    /// <summary>
    /// Opens a FAT disk image from a file on disk.
    /// </summary>
    /// <param name="imagePath">Path to the image file.</param>
    /// <returns>A new <see cref="FatFileSystem"/> backed by the file data.</returns>
    public static FatFileSystem FromFile(string imagePath) {
        return new FatFileSystem(File.ReadAllBytes(imagePath));
    }

    /// <summary>
    /// Lists all non-deleted, non-LFN entries in the root directory.
    /// </summary>
    public IReadOnlyList<FatDirectoryEntry> ListRootDirectory() {
        if (FatType == FatType.Fat32) {
            uint rootCluster = _bpb.RootCluster != 0 ? _bpb.RootCluster : 2;
            return ListSubDirectory(rootCluster);
        }
        return ReadDirectoryFromSectors(_bpb.RootDirStartSector, _bpb.RootDirEntries);
    }

    /// <summary>
    /// Lists all non-deleted, non-LFN entries in a subdirectory identified by its first cluster.
    /// </summary>
    /// <param name="firstCluster">The first cluster of the subdirectory.</param>
    public IReadOnlyList<FatDirectoryEntry> ListSubDirectory(uint firstCluster) {
        List<uint> chain = GetClusterChain(firstCluster);
        byte[] clusterData = ReadClusters(chain);
        return ParseDirectoryEntries(clusterData, clusterData.Length / FatDirectoryEntry.EntrySize);
    }

    /// <summary>
    /// Lists all non-deleted, non-LFN entries in a subdirectory identified by its first cluster.
    /// </summary>
    /// <param name="firstCluster">The first cluster (16-bit) of the subdirectory.</param>
    public IReadOnlyList<FatDirectoryEntry> ListSubDirectory(ushort firstCluster) {
        return ListSubDirectory((uint)firstCluster);
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
        uint firstCluster = entry.FirstCluster;
        List<uint> chain = GetClusterChain(firstCluster);
        byte[] allBytes = ReadClusters(chain);
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
    /// Resolves a DOS path within the FAT volume and returns the matching entry.
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
            currentDir = ListSubDirectory((uint)found.FirstCluster);
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

    private int GetDataStartSector() {
        int rootDirSectors = (_bpb.RootDirEntries * FatDirectoryEntry.EntrySize + _bpb.BytesPerSector - 1) / _bpb.BytesPerSector;
        int fatSectors = _bpb.NumberOfFats * (int)_bpb.SectorsPerFatEffective;
        return _bpb.ReservedSectors + fatSectors + rootDirSectors;
    }

    private FatType DetectFatType() {
        int dataStartSector = GetDataStartSector();
        uint totalSectors = _bpb.TotalSectors16 != 0 ? _bpb.TotalSectors16 : _bpb.TotalSectors32;
        uint dataSectors = totalSectors - (uint)dataStartSector;
        uint clusterCount = dataSectors / _bpb.SectorsPerCluster;
        if (clusterCount < 4085) {
            return FatType.Fat12;
        }
        if (clusterCount < 65525) {
            return FatType.Fat16;
        }
        return FatType.Fat32;
    }

    private uint[] ReadFat() {
        int fatByteOffset = _bpb.FatStartSector * _bpb.BytesPerSector;
        int fatByteLength = (int)(_bpb.SectorsPerFatEffective * _bpb.BytesPerSector);
        int dataStartSector = GetDataStartSector();
        int dataSectors = _bpb.TotalSectors - dataStartSector;
        int clusterCount = dataSectors / _bpb.SectorsPerCluster;
        int entryCount = clusterCount + 2;
        uint[] table = new uint[entryCount];

        if (FatType == FatType.Fat12) {
            for (int n = 0; n < entryCount; n++) {
                int byteIndex = fatByteOffset + (n * 12 / 8);
                if (byteIndex >= _imageData.Length) {
                    break;
                }
                if (byteIndex + 1 >= fatByteOffset + fatByteLength) {
                    break;
                }
                int rawWord = _imageData[byteIndex] | (_imageData[byteIndex + 1] << 8);
                if ((n & 1) == 0) {
                    table[n] = (uint)(rawWord & 0x0FFF);
                } else {
                    table[n] = (uint)((rawWord >> 4) & 0x0FFF);
                }
            }
        } else if (FatType == FatType.Fat16) {
            for (int n = 0; n < entryCount; n++) {
                int byteIndex = fatByteOffset + n * 2;
                if (byteIndex >= _imageData.Length) {
                    break;
                }
                if (byteIndex + 1 >= fatByteOffset + fatByteLength) {
                    break;
                }
                table[n] = BitConverter.ToUInt16(_imageData, byteIndex);
            }
        } else {
            for (int n = 0; n < entryCount; n++) {
                int byteIndex = fatByteOffset + n * 4;
                if (byteIndex >= _imageData.Length) {
                    break;
                }
                if (byteIndex + 3 >= fatByteOffset + fatByteLength) {
                    break;
                }
                table[n] = BitConverter.ToUInt32(_imageData, byteIndex) & 0x0FFFFFFF;
            }
        }
        return table;
    }

    private List<uint> GetClusterChain(uint startCluster) {
        List<uint> chain = new();
        uint current = startCluster;
        uint endOfChain;
        uint badCluster;
        if (FatType == FatType.Fat32) {
            endOfChain = Fat32EndOfChainMin;
            badCluster = Fat32BadCluster;
        } else if (FatType == FatType.Fat16) {
            endOfChain = Fat16EndOfChainMin;
            badCluster = Fat16BadCluster;
        } else {
            endOfChain = Fat12EndOfChainMin;
            badCluster = Fat12BadCluster;
        }
        while (current >= 2 && current < endOfChain && current != badCluster) {
            chain.Add(current);
            if (current >= (uint)_fat.Length) {
                break;
            }
            current = _fat[current];
        }
        return chain;
    }

    private byte[] ReadClusters(List<uint> chain) {
        int totalBytes = chain.Count * _bpb.BytesPerCluster;
        byte[] result = new byte[totalBytes];
        int offset = 0;
        foreach (uint cluster in chain) {
            int sectorStart = ClusterToSector(cluster);
            int byteOffset = sectorStart * _bpb.BytesPerSector;
            int bytesToCopy = _bpb.BytesPerCluster;
            if (byteOffset + bytesToCopy > _imageData.Length) {
                bytesToCopy = Math.Max(0, _imageData.Length - byteOffset);
            }
            if (bytesToCopy > 0) {
                Array.Copy(_imageData, byteOffset, result, offset, bytesToCopy);
            }
            offset += _bpb.BytesPerCluster;
        }
        return result;
    }

    private int ClusterToSector(uint cluster) {
        return GetDataStartSector() + (int)(cluster - 2) * _bpb.SectorsPerCluster;
    }

    private IReadOnlyList<FatDirectoryEntry> ReadDirectoryFromSectors(int startSector, int maxEntries) {
        List<FatDirectoryEntry> entries = new();
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
        return entries;
    }

    private static IReadOnlyList<FatDirectoryEntry> ParseDirectoryEntries(byte[] data, int maxEntries) {
        List<FatDirectoryEntry> entries = new();
        for (int i = 0; i < maxEntries; i++) {
            int entryOffset = i * FatDirectoryEntry.EntrySize;
            if (entryOffset + FatDirectoryEntry.EntrySize > data.Length) {
                break;
            }
            FatDirectoryEntry entry = FatDirectoryEntry.Parse(data.AsSpan(entryOffset, FatDirectoryEntry.EntrySize));
            if (entry.IsEndMarker) {
                break;
            }
            if (entry.IsDeleted || entry.IsLfn) {
                continue;
            }
            entries.Add(entry);
        }
        return entries;
    }

    private string FindVolumeLabel() {
        if (!string.IsNullOrWhiteSpace(_bpb.VolumeLabel) &&
            !string.Equals(_bpb.VolumeLabel, "NO NAME", StringComparison.OrdinalIgnoreCase)) {
            return _bpb.VolumeLabel;
        }
        if (FatType != FatType.Fat32) {
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
        }
        return string.Empty;
    }
}
