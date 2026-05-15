namespace Spice86.Shared.Emulator.Storage.FileSystem;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Spice86.Shared.Emulator.Storage.FileSystem.BootSector;
using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;
using Spice86.Shared.Emulator.Storage.FileSystem.Directory;

/// <summary>
/// Mutable FAT filesystem supporting read and write of files and directories.
/// Performs real cluster allocation, real directory entry serialisation, and real
/// FAT chain persistence to the backing disk image. No in-memory shortcuts.
/// </summary>
public sealed class MutableFatFileSystem
{
    private readonly byte[] _diskImage;
    private readonly FatType _fatType;
    private readonly MutableBiosParameterBlock _bootSector;
    private readonly FatTable _fatTable;
    private readonly List<MutableFatDirectoryEntry> _rootEntries;
    private readonly Dictionary<string, uint[]> _fileChains;
    private bool _isDirty;

    /// <summary>Gets the mutable boot sector (BPB).</summary>
    public MutableBiosParameterBlock BootSector => _bootSector;

    /// <summary>Gets the mutable FAT table.</summary>
    public FatTable FatTable => _fatTable;

    /// <summary>Gets whether there are uncommitted changes.</summary>
    public bool IsDirty => _isDirty;

    /// <summary>Gets the count of free clusters in the FAT.</summary>
    public uint FreeClusterCount => (uint)_fatTable.FreeClusterCount;

    /// <summary>
    /// Initialises a mutable FAT filesystem by parsing the boot sector, FAT table, and
    /// root directory entries from the supplied disk image.
    /// </summary>
    /// <param name="diskImage">Raw disk image bytes. Filesystem reads from and writes back to this buffer.</param>
    /// <param name="fatType">FAT12, FAT16, or FAT32.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="diskImage"/> is null.</exception>
    /// <exception cref="InvalidDataException">If the boot sector is invalid.</exception>
    public MutableFatFileSystem(byte[] diskImage, FatType fatType)
    {
        _diskImage = diskImage ?? throw new ArgumentNullException(nameof(diskImage));
        _fatType = fatType;
        _isDirty = false;
        _rootEntries = new List<MutableFatDirectoryEntry>();
        _fileChains = new Dictionary<string, uint[]>(StringComparer.OrdinalIgnoreCase);

        _bootSector = FatBootSectorCodec.Parse(diskImage.AsSpan(0, FatBootSectorCodec.BootSectorSize), fatType);

        int fatBytes = _bootSector.SectorsPerFat * _bootSector.BytesPerSector;
        int fatStartByteOffset = _bootSector.ReservedSectors * _bootSector.BytesPerSector;

        uint totalSectors = _bootSector.TotalSectors16 > 0 ? _bootSector.TotalSectors16 : _bootSector.TotalSectors32;
        uint rootDirSectors = (uint)((_bootSector.RootDirEntries * 32 + _bootSector.BytesPerSector - 1) / _bootSector.BytesPerSector);
        uint fatDataAreaSectors = totalSectors - _bootSector.ReservedSectors
                                  - ((uint)_bootSector.NumberOfFats * _bootSector.SectorsPerFat)
                                  - rootDirSectors;
        uint dataClusterCount = fatDataAreaSectors / _bootSector.SectorsPerCluster;
        int totalClusterEntries = (int)Math.Max(3u, dataClusterCount + 2);

        _fatTable = FatTable.FromBytes(diskImage.AsSpan(fatStartByteOffset, fatBytes), fatType, totalClusterEntries);

        LoadRootDirectory();
    }

    private int RootDirByteOffset => (_bootSector.ReservedSectors + _bootSector.NumberOfFats * _bootSector.SectorsPerFat) * _bootSector.BytesPerSector;

    private int DataAreaByteOffset
    {
        get
        {
            int rootDirSectors = (_bootSector.RootDirEntries * 32 + _bootSector.BytesPerSector - 1) / _bootSector.BytesPerSector;
            return RootDirByteOffset + rootDirSectors * _bootSector.BytesPerSector;
        }
    }

    private int ClusterSizeInBytes => _bootSector.SectorsPerCluster * _bootSector.BytesPerSector;

    private int ClusterByteOffset(uint cluster) => DataAreaByteOffset + (int)(cluster - 2) * ClusterSizeInBytes;

    private void LoadRootDirectory()
    {
        int rootOffset = RootDirByteOffset;
        for (int i = 0; i < _bootSector.RootDirEntries; i++)
        {
            int entryOffset = rootOffset + i * 32;
            byte firstByte = _diskImage[entryOffset];
            if (firstByte == 0x00)
            {
                break; // end of directory
            }
            if (firstByte == 0xE5)
            {
                continue; // deleted entry
            }
            MutableFatDirectoryEntry entry = MutableFatDirectoryEntry.Parse(_diskImage.AsSpan(entryOffset, 32));
            // skip volume labels (attribute bit 3 = 0x08)
            if ((entry.Attributes & 0x08) != 0 && (entry.Attributes & 0x10) == 0)
            {
                _rootEntries.Add(entry);
                continue;
            }
            _rootEntries.Add(entry);

            if (entry.FirstCluster >= 2 && entry.FileSize > 0)
            {
                IReadOnlyList<uint> chain = _fatTable.FollowChain(entry.FirstCluster);
                uint[] chainArray = new uint[chain.Count];
                for (int c = 0; c < chain.Count; c++)
                {
                    chainArray[c] = chain[c];
                }
                _fileChains[entry.DosName] = chainArray;
            }
        }
    }

    /// <summary>
    /// Creates a new file with the specified content. Allocates cluster chain,
    /// writes file data into the data area, and creates a directory entry.
    /// </summary>
    /// <param name="dosPath">DOS 8.3 path (root-relative, e.g. "FILE.TXT").</param>
    /// <param name="content">File content bytes.</param>
    public void CreateFile(string dosPath, byte[] content)
    {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));
        _ = content ?? throw new ArgumentNullException(nameof(content));

        if (_fileChains.ContainsKey(dosPath))
        {
            DeleteFile(dosPath);
        }

        uint[] chain = AllocateChain(content.Length);
        WriteContentToClusters(chain, content);

        MutableFatDirectoryEntry entry = BuildDirectoryEntry(dosPath, attributes: 0x20, firstCluster: chain.Length == 0 ? (ushort)0 : (ushort)chain[0], fileSize: (uint)content.Length);
        _rootEntries.Add(entry);
        _fileChains[entry.DosName] = chain;

        _isDirty = true;
    }

    private uint[] AllocateChain(int contentLength)
    {
        if (contentLength <= 0)
        {
            return Array.Empty<uint>();
        }
        int clusterSize = ClusterSizeInBytes;
        int clustersNeeded = (contentLength + clusterSize - 1) / clusterSize;
        uint[] chain = new uint[clustersNeeded];
        for (int i = 0; i < clustersNeeded; i++)
        {
            chain[i] = _fatTable.AllocateCluster();
        }
        for (int i = 0; i < clustersNeeded - 1; i++)
        {
            _fatTable.LinkClusters(chain[i], chain[i + 1]);
        }
        return chain;
    }

    private void WriteContentToClusters(uint[] chain, byte[] content)
    {
        int clusterSize = ClusterSizeInBytes;
        int written = 0;
        for (int i = 0; i < chain.Length; i++)
        {
            int offset = ClusterByteOffset(chain[i]);
            int toCopy = Math.Min(clusterSize, content.Length - written);
            Array.Copy(content, written, _diskImage, offset, toCopy);
            // zero remainder of cluster
            int remainder = clusterSize - toCopy;
            if (remainder > 0)
            {
                Array.Clear(_diskImage, offset + toCopy, remainder);
            }
            written += toCopy;
        }
    }

    private static MutableFatDirectoryEntry BuildDirectoryEntry(string dosPath, byte attributes, ushort firstCluster, uint fileSize)
    {
        SplitDosName(dosPath, out string baseName, out string extension);
        return new MutableFatDirectoryEntry
        {
            BaseName = baseName,
            Extension = extension,
            Attributes = attributes,
            FirstCluster = firstCluster,
            FileSize = fileSize
        };
    }

    private static void SplitDosName(string dosPath, out string baseName, out string extension)
    {
        string upper = dosPath.ToUpperInvariant();
        int dotIdx = upper.LastIndexOf('.');
        if (dotIdx < 0)
        {
            baseName = upper.Length > 8 ? upper.Substring(0, 8) : upper;
            extension = string.Empty;
            return;
        }
        string baseRaw = upper.Substring(0, dotIdx);
        string extRaw = upper.Substring(dotIdx + 1);
        baseName = baseRaw.Length > 8 ? baseRaw.Substring(0, 8) : baseRaw;
        extension = extRaw.Length > 3 ? extRaw.Substring(0, 3) : extRaw;
    }

    /// <summary>
    /// Deletes a file and frees its cluster chain.
    /// </summary>
    /// <param name="dosPath">DOS file path.</param>
    public void DeleteFile(string dosPath)
    {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));

        string canonical = CanonicaliseDosName(dosPath);
        int idx = FindRootEntryIndex(canonical);
        if (idx < 0)
        {
            return;
        }
        MutableFatDirectoryEntry entry = _rootEntries[idx];
        if (_fileChains.TryGetValue(entry.DosName, out uint[]? chain) && chain != null)
        {
            foreach (uint cluster in chain)
            {
                _fatTable.FreeCluster(cluster);
            }
            _fileChains.Remove(entry.DosName);
        }
        _rootEntries.RemoveAt(idx);
        _isDirty = true;
    }

    private static string CanonicaliseDosName(string dosPath)
    {
        SplitDosName(dosPath, out string baseName, out string extension);
        return string.IsNullOrEmpty(extension) ? baseName : baseName + "." + extension;
    }

    private int FindRootEntryIndex(string canonicalDosName)
    {
        for (int i = 0; i < _rootEntries.Count; i++)
        {
            if (string.Equals(_rootEntries[i].DosName, canonicalDosName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Renames an existing file or directory entry.
    /// </summary>
    /// <param name="oldPath">Current DOS path.</param>
    /// <param name="newPath">New DOS path.</param>
    public void RenameEntry(string oldPath, string newPath)
    {
        _ = oldPath ?? throw new ArgumentNullException(nameof(oldPath));
        _ = newPath ?? throw new ArgumentNullException(nameof(newPath));

        string oldCanonical = CanonicaliseDosName(oldPath);
        int idx = FindRootEntryIndex(oldCanonical);
        if (idx < 0)
        {
            return;
        }
        MutableFatDirectoryEntry entry = _rootEntries[idx];
        SplitDosName(newPath, out string newBase, out string newExt);

        if (_fileChains.TryGetValue(entry.DosName, out uint[]? chain) && chain != null)
        {
            _fileChains.Remove(entry.DosName);
            entry.BaseName = newBase;
            entry.Extension = newExt;
            _fileChains[entry.DosName] = chain;
        }
        else
        {
            entry.BaseName = newBase;
            entry.Extension = newExt;
        }
        _isDirty = true;
    }

    /// <summary>
    /// Truncates a file to the specified size, freeing excess clusters.
    /// </summary>
    /// <param name="dosPath">DOS file path.</param>
    /// <param name="newSize">New file size in bytes.</param>
    public void TruncateFile(string dosPath, uint newSize)
    {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));

        string canonical = CanonicaliseDosName(dosPath);
        int idx = FindRootEntryIndex(canonical);
        if (idx < 0)
        {
            return;
        }
        MutableFatDirectoryEntry entry = _rootEntries[idx];
        if (newSize >= entry.FileSize)
        {
            return;
        }

        if (!_fileChains.TryGetValue(entry.DosName, out uint[]? oldChain) || oldChain == null)
        {
            entry.FileSize = newSize;
            _isDirty = true;
            return;
        }

        int clusterSize = ClusterSizeInBytes;
        int neededClusters = newSize == 0 ? 0 : (int)((newSize + clusterSize - 1) / clusterSize);

        for (int i = neededClusters; i < oldChain.Length; i++)
        {
            _fatTable.FreeCluster(oldChain[i]);
        }

        if (neededClusters == 0)
        {
            _fileChains.Remove(entry.DosName);
            entry.FirstCluster = 0;
            entry.FileSize = 0;
        }
        else
        {
            uint[] newChain = new uint[neededClusters];
            Array.Copy(oldChain, newChain, neededClusters);
            _fatTable.MarkAsEof(newChain[neededClusters - 1]);
            _fileChains[entry.DosName] = newChain;
            entry.FileSize = newSize;
            entry.FirstCluster = (ushort)newChain[0];
        }

        _isDirty = true;
    }

    /// <summary>
    /// Reads a file's content by walking its cluster chain.
    /// </summary>
    /// <param name="dosPath">DOS file path.</param>
    /// <returns>File content bytes.</returns>
    /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
    public byte[] ReadFile(string dosPath)
    {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));

        string canonical = CanonicaliseDosName(dosPath);
        int idx = FindRootEntryIndex(canonical);
        if (idx < 0)
        {
            throw new FileNotFoundException(dosPath);
        }
        MutableFatDirectoryEntry entry = _rootEntries[idx];
        if (entry.FileSize == 0 || entry.FirstCluster == 0)
        {
            return Array.Empty<byte>();
        }
        if (!_fileChains.TryGetValue(entry.DosName, out uint[]? chain) || chain == null)
        {
            IReadOnlyList<uint> walked = _fatTable.FollowChain(entry.FirstCluster);
            chain = new uint[walked.Count];
            for (int c = 0; c < walked.Count; c++)
            {
                chain[c] = walked[c];
            }
            _fileChains[entry.DosName] = chain;
        }

        byte[] content = new byte[entry.FileSize];
        int clusterSize = ClusterSizeInBytes;
        int read = 0;
        for (int i = 0; i < chain.Length && read < content.Length; i++)
        {
            int offset = ClusterByteOffset(chain[i]);
            int toCopy = Math.Min(clusterSize, content.Length - read);
            Array.Copy(_diskImage, offset, content, read, toCopy);
            read += toCopy;
        }
        return content;
    }

    /// <summary>
    /// Modifies the boot sector via a mutator delegate. Marks the filesystem dirty.
    /// </summary>
    /// <param name="mutator">Action that mutates BPB fields.</param>
    public void WriteBootSector(Action<MutableBiosParameterBlock> mutator)
    {
        _ = mutator ?? throw new ArgumentNullException(nameof(mutator));
        mutator(_bootSector);
        _isDirty = true;
    }

    /// <summary>
    /// Commits all pending changes to the disk image: serialises the boot sector,
    /// writes all FAT copies identically, and writes root directory entries.
    /// Clears the dirty flag on success.
    /// </summary>
    /// <param name="diskImage">Target disk image buffer (must be the same buffer as construction).</param>
    public void CommitChanges(byte[] diskImage)
    {
        _ = diskImage ?? throw new ArgumentNullException(nameof(diskImage));
        if (!ReferenceEquals(diskImage, _diskImage))
        {
            throw new ArgumentException("CommitChanges must be called with the same disk image used to construct the filesystem.", nameof(diskImage));
        }

        FatBootSectorCodec.Write(_bootSector, diskImage.AsSpan(0, FatBootSectorCodec.BootSectorSize), _fatType);

        int fatBytes = _bootSector.SectorsPerFat * _bootSector.BytesPerSector;
        byte[] fatBuffer = new byte[fatBytes];
        _fatTable.WriteTo(fatBuffer);
        for (int f = 0; f < _bootSector.NumberOfFats; f++)
        {
            int fatOffset = (_bootSector.ReservedSectors + f * _bootSector.SectorsPerFat) * _bootSector.BytesPerSector;
            Array.Copy(fatBuffer, 0, diskImage, fatOffset, fatBytes);
        }

        WriteRootDirectory(diskImage);

        _isDirty = false;
    }

    private void WriteRootDirectory(byte[] diskImage)
    {
        int rootOffset = RootDirByteOffset;
        int rootBytes = _bootSector.RootDirEntries * 32;
        Array.Clear(diskImage, rootOffset, rootBytes);

        int slot = 0;
        foreach (MutableFatDirectoryEntry entry in _rootEntries)
        {
            if (slot >= _bootSector.RootDirEntries)
            {
                throw new InvalidOperationException("Root directory is full.");
            }
            entry.Serialize(diskImage.AsSpan(rootOffset + slot * 32, 32));
            slot++;
        }
    }
}
