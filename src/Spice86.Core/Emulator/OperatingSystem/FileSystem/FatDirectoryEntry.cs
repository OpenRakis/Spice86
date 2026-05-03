namespace Spice86.Core.Emulator.OperatingSystem.FileSystem;

using System;
using System.Text;

/// <summary>
/// Represents a 32-byte FAT directory entry (standard format for FAT12/FAT16/FAT32).
/// </summary>
public sealed class FatDirectoryEntry {
    /// <summary>The size in bytes of a single directory entry on disk.</summary>
    public const int EntrySize = 32;

    /// <summary>The first byte value that signals the end of directory entries.</summary>
    public const byte EndOfDirectory = 0x00;

    /// <summary>The first byte value that signals a deleted entry.</summary>
    public const byte DeletedEntry = 0xE5;

    /// <summary>Gets the base name of the file (8 characters, space-padded).</summary>
    public string BaseName { get; }

    /// <summary>Gets the file extension (3 characters, space-padded).</summary>
    public string Extension { get; }

    /// <summary>Gets the DOS attribute byte.</summary>
    public byte Attributes { get; }

    /// <summary>Gets the first cluster of the file or directory data.</summary>
    public ushort FirstCluster { get; }

    /// <summary>Gets the file size in bytes (0 for directories and volume labels).</summary>
    public uint FileSize { get; }

    /// <summary>Returns <see langword="true"/> if this entry marks the end of the directory table.</summary>
    public bool IsEndMarker { get; }

    /// <summary>Returns <see langword="true"/> if this entry is for a deleted file.</summary>
    public bool IsDeleted { get; }

    /// <summary>Returns <see langword="true"/> if this entry is a subdirectory.</summary>
    public bool IsDirectory => (Attributes & 0x10) != 0;

    /// <summary>Returns <see langword="true"/> if this entry is a volume label.</summary>
    public bool IsVolumeLabel => (Attributes & 0x08) != 0;

    /// <summary>Returns <see langword="true"/> if this is a long-file-name (LFN) entry.</summary>
    public bool IsLfn => Attributes == 0x0F;

    /// <summary>
    /// Gets the 8.3 DOS filename (without leading space or dot notation).
    /// Returns an empty string for volume labels and end-markers.
    /// </summary>
    public string DosName {
        get {
            if (IsEndMarker || IsDeleted || IsVolumeLabel || IsLfn) {
                return string.Empty;
            }
            string name = BaseName.TrimEnd();
            string ext = Extension.TrimEnd();
            if (string.IsNullOrEmpty(ext)) {
                return name;
            }
            return $"{name}.{ext}";
        }
    }

    private FatDirectoryEntry(string baseName, string extension, byte attributes,
        ushort firstCluster, uint fileSize, bool isEndMarker, bool isDeleted) {
        BaseName = baseName;
        Extension = extension;
        Attributes = attributes;
        FirstCluster = firstCluster;
        FileSize = fileSize;
        IsEndMarker = isEndMarker;
        IsDeleted = isDeleted;
    }

    /// <summary>
    /// Parses a <see cref="FatDirectoryEntry"/> from a 32-byte span.
    /// </summary>
    /// <param name="data">Exactly 32 bytes of raw directory-entry data.</param>
    /// <returns>The parsed entry.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="data"/> is not exactly 32 bytes.</exception>
    public static FatDirectoryEntry Parse(ReadOnlySpan<byte> data) {
        if (data.Length < EntrySize) {
            throw new ArgumentException($"Directory entry must be {EntrySize} bytes (got {data.Length}).", nameof(data));
        }

        byte firstByte = data[0];
        if (firstByte == EndOfDirectory) {
            return new FatDirectoryEntry(string.Empty, string.Empty, 0, 0, 0, isEndMarker: true, isDeleted: false);
        }

        bool isDeleted = firstByte == DeletedEntry;
        // Restore the real first character for deleted entries (DOS stores 0xE5 there).
        byte[] nameBytes = data.Slice(0, 8).ToArray();
        if (isDeleted) {
            nameBytes[0] = (byte)'?';
        }

        string baseName = Encoding.ASCII.GetString(nameBytes);
        string extension = Encoding.ASCII.GetString(data.Slice(8, 3));
        byte attributes = data[11];
        ushort firstCluster = BitConverter.ToUInt16(data.Slice(26, 2));
        uint fileSize = BitConverter.ToUInt32(data.Slice(28, 4));

        return new FatDirectoryEntry(baseName, extension, attributes, firstCluster, fileSize, isEndMarker: false, isDeleted);
    }
}
