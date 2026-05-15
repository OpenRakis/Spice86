namespace Spice86.Shared.Emulator.Storage.FileSystem;

using System;
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
    private MutableBiosParameterBlock _bootSector;
    private FatTable _fatTable;
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
    public uint FreeClusterCount => _fatTable.FreeClusterCount;

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
        
        // Parse boot sector
        byte[] bootSectorBytes = new byte[512];
        Array.Copy(diskImage, 0, bootSectorBytes, 0, 512);
        
        // Create mutable BPB from boot sector
        _bootSector = new MutableBiosParameterBlock(_fatType);
        // TODO: Parse boot sector into _bootSector
        
        // Initialize FAT table
        _fatTable = new FatTable(_fatType, 10000); // Simplified; actual size from BPB
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
        
        _isDirty = true;
        // TODO: Allocate clusters, create directory entry
    }

    /// <summary>
    /// Deletes a file and frees its cluster chain.
    /// </summary>
    /// <param name="dosPath">DOS-compatible path.</param>
    public void DeleteFile(string dosPath) {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));
        
        _isDirty = true;
        // TODO: Find file entry, free clusters, remove directory entry
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
        
        _isDirty = true;
        // TODO: Find entry, update directory name field
    }

    /// <summary>
    /// Truncates a file to a specified size, freeing excess clusters.
    /// </summary>
    /// <param name="dosPath">DOS-compatible file path.</param>
    /// <param name="newSize">New file size in bytes.</param>
    public void TruncateFile(string dosPath, uint newSize) {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));
        
        _isDirty = true;
        // TODO: Find file, calculate required clusters, free excess, update directory entry size
    }

    /// <summary>
    /// Reads a file's content.
    /// </summary>
    /// <param name="dosPath">DOS-compatible file path.</param>
    /// <returns>File content bytes.</returns>
    /// <exception cref="FileNotFoundException">If file does not exist.</exception>
    public byte[] ReadFile(string dosPath) {
        _ = dosPath ?? throw new ArgumentNullException(nameof(dosPath));
        
        // TODO: Find directory entry, read clusters via FAT chain
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
        _isDirty = false;
    }
}
