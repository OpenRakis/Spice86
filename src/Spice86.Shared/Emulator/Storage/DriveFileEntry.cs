namespace Spice86.Shared.Emulator.Storage;

using System.Collections.Generic;

/// <summary>
/// Represents a single file or directory entry as seen by DOS in a mounted drive.
/// </summary>
public sealed class DriveFileEntry {
    /// <summary>Gets the DOS 8.3 filename or directory name.</summary>
    public string Name { get; }

    /// <summary>Gets the file size in bytes, or zero for directories.</summary>
    public long Size { get; }

    /// <summary>Gets the DOS attribute flags as a string (e.g. "Directory, ReadOnly").</summary>
    public string Attributes { get; }

    /// <summary>Gets a value indicating whether this entry is a directory.</summary>
    public bool IsDirectory { get; }

    /// <summary>Gets the child entries for directories, or an empty list for files.</summary>
    public IReadOnlyList<DriveFileEntry> Children { get; }

    /// <summary>Initialises a new file entry.</summary>
    public DriveFileEntry(string name, long size, string attributes, bool isDirectory, IReadOnlyList<DriveFileEntry> children) {
        Name = name;
        Size = size;
        Attributes = attributes;
        IsDirectory = isDirectory;
        Children = children;
    }
}
