namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using System;
using System.Text;

using Spice86.Shared.Emulator.Storage.FileSystem;

/// <summary>
/// Mutable representation of a 32-byte FAT directory entry.
/// </summary>
public sealed class MutableFatDirectoryEntry
{
    /// <summary>Gets or sets the 8.3 base name (1 to 8 ASCII characters).</summary>
    public string BaseName { get; set; } = string.Empty;

    /// <summary>Gets or sets the 8.3 extension (0 to 3 ASCII characters).</summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>Gets or sets the DOS attribute byte.</summary>
    public byte Attributes { get; set; }

    /// <summary>Gets or sets the first data cluster.</summary>
    public ushort FirstCluster { get; set; }

    /// <summary>Gets or sets the file size in bytes.</summary>
    public uint FileSize { get; set; }

    /// <summary>
    /// Gets or sets the optional VFAT long file name. When non-empty,
    /// directory writers emit a preceding LFN slot chain. The short name
    /// (<see cref="BaseName"/>/<see cref="Extension"/>) remains authoritative
    /// for legacy DOS callers and FAT chain bookkeeping.
    /// </summary>
    public string LongName { get; set; } = string.Empty;

    /// <summary>Gets the normalised 8.3 DOS name.</summary>
    public string DosName
    {
        get
        {
            string normalizedBaseName = BaseName.TrimEnd();
            string normalizedExtension = Extension.TrimEnd();
            if (string.IsNullOrEmpty(normalizedExtension))
            {
                return normalizedBaseName;
            }
            return normalizedBaseName + "." + normalizedExtension;
        }
    }

    /// <summary>
    /// Parses a mutable FAT entry from raw bytes.
    /// </summary>
    /// <param name="entryBytes">Raw entry bytes (32 bytes minimum).</param>
    /// <returns>Parsed mutable entry.</returns>
    public static MutableFatDirectoryEntry Parse(ReadOnlySpan<byte> entryBytes)
    {
        FatDirectoryEntry entry = FatDirectoryEntry.Parse(entryBytes);
        MutableFatDirectoryEntry mutableEntry = new MutableFatDirectoryEntry
        {
            BaseName = entry.BaseName.TrimEnd(),
            Extension = entry.Extension.TrimEnd(),
            Attributes = entry.Attributes,
            FirstCluster = entry.FirstCluster,
            FileSize = entry.FileSize
        };
        return mutableEntry;
    }

    /// <summary>
    /// Serialises the entry to the provided destination span.
    /// </summary>
    /// <param name="destination">Destination span with 32 bytes minimum.</param>
    public void Serialize(Span<byte> destination)
    {
        if (destination.Length < FatDirectoryEntry.EntrySize)
        {
            throw new ArgumentException("Directory entry destination must be at least 32 bytes.", nameof(destination));
        }

        ValidateNamePart(BaseName, 8, nameof(BaseName));
        ValidateNamePart(Extension, 3, nameof(Extension));

        destination.Slice(0, FatDirectoryEntry.EntrySize).Clear();

        string paddedBaseName = BaseName.ToUpperInvariant().PadRight(8);
        string paddedExtension = Extension.ToUpperInvariant().PadRight(3);

        Encoding.ASCII.GetBytes(paddedBaseName).CopyTo(destination);
        Encoding.ASCII.GetBytes(paddedExtension).CopyTo(destination.Slice(8));

        destination[11] = Attributes;
        BitConverter.GetBytes(FirstCluster).CopyTo(destination.Slice(26, 2));
        BitConverter.GetBytes(FileSize).CopyTo(destination.Slice(28, 4));
    }

    private static void ValidateNamePart(string value, int maxLength, string parameterName)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException("Name part exceeds DOS 8.3 limit.", parameterName);
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (!DosNameConverter.IsAllowedDosCharacter(value[i]))
            {
                throw new ArgumentException("Name part contains unsupported DOS character.", parameterName);
            }
        }
    }
}
