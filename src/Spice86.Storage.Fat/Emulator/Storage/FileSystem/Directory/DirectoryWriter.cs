namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using System;

using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;

/// <summary>
/// Writes mutable FAT entries into directory sector buffers.
/// </summary>
public sealed class DirectoryWriter {
    /// <summary>
    /// Finds the next usable slot in a directory sector buffer.
    /// </summary>
    /// <param name="directorySectors">Raw directory sector bytes.</param>
    /// <returns>Slot index, or -1 if the directory has no free slot.</returns>
    public static int FindNextSlot(byte[] directorySectors) {
        if (directorySectors == null) {
            throw new ArgumentNullException(nameof(directorySectors));
        }

        int entryCount = directorySectors.Length / FatDirectoryEntry.EntrySize;
        for (int slot = 0; slot < entryCount; slot++) {
            int offset = slot * FatDirectoryEntry.EntrySize;
            byte marker = directorySectors[offset];
            if (marker is FatDirectoryEntry.EndOfDirectory or FatDirectoryEntry.DeletedEntry) {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>
    /// Writes one entry to an existing slot.
    /// </summary>
    /// <param name="directorySectors">Raw directory bytes.</param>
    /// <param name="slot">Slot index to overwrite.</param>
    /// <param name="entry">Entry to write.</param>
    /// <param name="fatTable">FAT table used for chain validation.</param>
    public static void WriteEntry(byte[] directorySectors, int slot, MutableFatDirectoryEntry entry, FatTable fatTable) {
        if (directorySectors == null) {
            throw new ArgumentNullException(nameof(directorySectors));
        }

        if (entry == null) {
            throw new ArgumentNullException(nameof(entry));
        }

        if (fatTable == null) {
            throw new ArgumentNullException(nameof(fatTable));
        }

        if (slot < 0) {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        int byteOffset = slot * FatDirectoryEntry.EntrySize;
        if (byteOffset + FatDirectoryEntry.EntrySize > directorySectors.Length) {
            throw new ArgumentOutOfRangeException(nameof(slot), "Slot is outside the directory buffer.");
        }

        if (entry.FirstCluster >= 2 && !fatTable.IsFree(entry.FirstCluster) && !fatTable.IsEndOfChain(entry.FirstCluster)) {
            fatTable.MarkAsEof(entry.FirstCluster);
        }

        Span<byte> destination = directorySectors.AsSpan(byteOffset, FatDirectoryEntry.EntrySize);
        FatDirectoryEntryCodec.Write(entry, destination);
    }
}
