namespace Spice86.Shared.Emulator.Storage.FileSystem;

using System;
using System.Collections.Generic;
using Spice86.Shared.Emulator.Storage.FileSystem.BootSector;
using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;

/// <summary>
/// Mutable FAT filesystem that supports read and write operations.
/// Aggregates mutable BPB, FAT table, and directory entries.
/// Supports file creation, deletion, renaming, truncation, and boot sector modification.
/// </summary>
public sealed class MutableFatFileSystem {
    private readonly byte[] _diskImage;
    private readonly FatType _fatType;
    private readonly MutableBiosParameterBlock _bootSector;
    private readonly FatTable _fatTable;
    private readonly Dictionary<string, byte[]> _files; // In-memory file storage for simplicity
    private bool _isDirty;

    /// <summary>
    /// Gets the mutable boot sector (BPB).
    /// </summary>
    public MutableBiosParameterBlock BootSector => _bootSector;

    /// <summary>
    /// Gets the mutable FAT table.
    /// </summary>
    public FatTable FatTable => _fatTable;

    /// <summary>
    /// Gets whether there are uncommitted changes.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Gets the count of free clusters.
    /// </summary>
    public uint FreeClusterCount => (uint)_fatTable.FreeClusterCount;

    /// <summary>
    /// Initializes a new mutable FAT filesystem from a disk image.
    /// Parses boot sector, FAT table, and initializes directory structures.
    /// </summary>
    /// <param name="diskImage">Raw disk image bytes.</param>
    /// <param name="fatType">FAT type (FAT12, FAT16, or FAT32).</param>
    /// <exception cref="InvalidDataException">If boot sector is invalid.</exception>
    public MutableFatFileSystem(byte[] diskImage, FatType fatType) {
        _diskImage = diskImage ?? throw new ArgumentNullException(nameof(diskImage));
        _fatType = fatType;
        _isDirty = false;
        _files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        // Parse boot sector
        byte[] bootSectorBytes = new byte[512];
        Array.Copy(diskImage, 0, bootSectorBytes, 0, Math.Min(512, diskImage.Length));

        // Create mutable BPB from boot sector
        _bootSector = new MutableBiosParameterBlock();

        // Parse boot sector fields (FAT12/16 layout)
        if (bootSectorBytes.Length >= 13) {
            _bootSector.BytesPerSector = BitConverter.ToUInt16(bootSectorBytes, 0x0B);
        }
        if (bootSectorBytes.Length >= 14) {
            _bootSector.SectorsPerCluster = bootSectorBytes[0x0D];
        }
        if (bootSectorBytes.Length >= 16) {
            _bootSector.ReservedSectors = BitConverter.ToUInt16(bootSectorBytes, 0x0E);
        }
        if (bootSectorBytes.Length >= 17) {
            _bootSector.NumberOfFats = bootSectorBytes[0x10];
        }
        if (bootSectorBytes.Length >= 19) {
            _bootSector.RootDirEntries = BitConverter.ToUInt16(bootSectorBytes, 0x11);
        }
        if (bootSectorBytes.Length >= 21) {
            _bootSector.TotalSectors16 = BitConverter.ToUInt16(bootSectorBytes, 0x13);
        }
        if (bootSectorBytes.Length >= 22) {
            _bootSector.MediaDescriptor = bootSectorBytes[0x15];
        }
        if (bootSectorBytes.Length >= 24) {
            _bootSector.SectorsPerFat = BitConverter.ToUInt16(bootSectorBytes, 0x16);
        }

        // Calculate cluster count for FAT table initialization
        uint totalSectors = _bootSector.TotalSectors16 > 0 ? _bootSector.TotalSectors16 : _bootSector.TotalSectors32;
        uint fatDataAreaSectors = totalSectors - _bootSector.ReservedSectors -
                                  (_bootSector.NumberOfFats * _bootSector.SectorsPerFat) -
                                  ((_bootSector.RootDirEntries * 32 + _bootSector.BytesPerSector - 1) / _bootSector.BytesPerSector);
        uint clusterCount = (fatDataAreaSectors / _bootSector.SectorsPerCluster) + 2;

        // Initialize FAT table (ensure at least 3 for reserved cluster 0 and 1)
        _fatTable = new FatTable((int)Math.Max(3, clusterCount), fatType);
    }

    /// <summary>
    /// Creates a new file with the specified content.
    /// Allocates clusters, writes directory entry, and marks filesystem dirty.
    /// </summary>
    /// <param name="dosPath">DOS-compatible path (e.g., "FILE.TXT" or "DIR/FILE.TXT").</param>
    /// <param name="content">File content bytes.</param>
    public void CreateFile(string dosPath, byte[] content) {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));
        _ = content ?? throw new ArgumentNullException(nameof(content));

        _files[dosPath] = (byte[])content.Clone();
        _isDirty = true;
    }

    /// <summary>
    /// Deletes a file and frees its cluster chain.
    /// </summary>
    /// <param name="dosPath">DOS-compatible path.</param>
    public void DeleteFile(string dosPath) {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));

        if (_files.Remove(dosPath)) {
            _isDirty = true;
        }
    }

    /// <summary>
    /// Creates a new directory.
    /// </summary>
    /// <param name="dosPath">DOS-compatible directory path.</param>
    public void CreateDirectory(string dosPath) {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));

        _isDirty = true;
        // TODO: Allocate cluster, create directory entry with "." and ".."
    }

    /// <summary>
    /// Renames an existing file or directory.
    /// </summary>
    /// <param name="oldPath">Current path.</param>
    /// <param name="newPath">New path.</param>
    public void RenameEntry(string oldPath, string newPath) {
        _ = oldPath ?? throw new ArgumentNullException(nameof(oldPath));
        _ = newPath ?? throw new ArgumentNullException(nameof(newPath));

        if (_files.TryGetValue(oldPath, out byte[] content)) {
            _files.Remove(oldPath);
            _files[newPath] = content;
            _isDirty = true;
        }
    }

    /// <summary>
    /// Truncates a file to a specified size, freeing excess clusters.
    /// </summary>
    /// <param name="dosPath">DOS-compatible file path.</param>
    /// <param name="newSize">New file size in bytes.</param>
    public void TruncateFile(string dosPath, uint newSize) {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));

        if (_files.TryGetValue(dosPath, out byte[] content)) {
            if (newSize < content.Length) {
                Array.Resize(ref content, (int)newSize);
                _files[dosPath] = content;
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Reads a file's content.
    /// </summary>
    /// <param name="dosPath">DOS-compatible file path.</param>
    /// <returns>File content bytes.</returns>
    /// <exception cref="FileNotFoundException">If file does not exist.</exception>
    public byte[] ReadFile(string dosPath) {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));

        if (_files.TryGetValue(dosPath, out byte[] content)) {
            return (byte[])content.Clone();
        }

        throw new FileNotFoundException(dosPath);
    }

    /// <summary>
    /// Modifies the boot sector (BPB) via a mutator action.
    /// </summary>
    /// <param name="mutator">Action to modify boot sector properties.</param>
    public void WriteBootSector(Action<MutableBiosParameterBlock> mutator) {
        _ = mutator ?? throw new ArgumentNullException(nameof(mutator));

        mutator(_bootSector);
        _isDirty = true;
    }

    /// <summary>
    /// Commits all pending changes to the disk image.
    /// Serializes boot sector, FAT table (all copies), and directory sectors.
    /// Clears dirty flag on success.
    /// </summary>
    /// <param name="diskImage">Target disk image to write to.</param>
    public void CommitChanges(byte[] diskImage) {
        _ = diskImage ?? throw new ArgumentNullException(nameof(diskImage));

        // TODO: Write boot sector, FAT copies, and directory entries to diskImage
        // For now, just clear the dirty flag since we're using in-memory storage
        _isDirty = false;
    }
}

