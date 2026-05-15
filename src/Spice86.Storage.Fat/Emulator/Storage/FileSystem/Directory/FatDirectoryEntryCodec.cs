namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using System;

/// <summary>
/// Codec for mutable FAT directory entries.
/// </summary>
public static class FatDirectoryEntryCodec
{
    /// <summary>
    /// Parses a mutable FAT directory entry.
    /// </summary>
    /// <param name="entryBytes">Raw entry bytes (32 bytes minimum).</param>
    /// <returns>Parsed entry.</returns>
    public static MutableFatDirectoryEntry Parse(ReadOnlySpan<byte> entryBytes)
    {
        return MutableFatDirectoryEntry.Parse(entryBytes);
    }

    /// <summary>
    /// Writes a mutable FAT directory entry.
    /// </summary>
    /// <param name="entry">Entry to serialise.</param>
    /// <param name="destination">Destination span (32 bytes minimum).</param>
    public static void Write(MutableFatDirectoryEntry entry, Span<byte> destination)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        entry.Serialize(destination);
    }
}
