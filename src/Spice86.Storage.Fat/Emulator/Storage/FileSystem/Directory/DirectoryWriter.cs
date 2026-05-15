namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using System;
using System.Collections.Generic;
using System.Text;

using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;
using Spice86.Shared.Emulator.Storage.FileSystem.Directory.LongFileName;

/// <summary>
/// Writes mutable FAT entries into directory sector buffers.
/// </summary>
public sealed class DirectoryWriter
{
    /// <summary>
    /// Finds the next usable slot in a directory sector buffer.
    /// </summary>
    /// <param name="directorySectors">Raw directory sector bytes.</param>
    /// <returns>Slot index, or -1 if the directory has no free slot.</returns>
    public int FindNextSlot(byte[] directorySectors)
    {
        if (directorySectors == null)
        {
            throw new ArgumentNullException(nameof(directorySectors));
        }

        int entryCount = directorySectors.Length / FatDirectoryEntry.EntrySize;
        for (int slot = 0; slot < entryCount; slot++)
        {
            int offset = slot * FatDirectoryEntry.EntrySize;
            byte marker = directorySectors[offset];
            if (marker == FatDirectoryEntry.EndOfDirectory || marker == FatDirectoryEntry.DeletedEntry)
            {
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
    public void WriteEntry(byte[] directorySectors, int slot, MutableFatDirectoryEntry entry, FatTable fatTable)
    {
        if (directorySectors == null)
        {
            throw new ArgumentNullException(nameof(directorySectors));
        }

        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (fatTable == null)
        {
            throw new ArgumentNullException(nameof(fatTable));
        }

        if (slot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        int byteOffset = slot * FatDirectoryEntry.EntrySize;
        if (byteOffset + FatDirectoryEntry.EntrySize > directorySectors.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), "Slot is outside the directory buffer.");
        }

        if (entry.FirstCluster >= 2 && !fatTable.IsFree(entry.FirstCluster) && !fatTable.IsEndOfChain(entry.FirstCluster))
        {
            fatTable.MarkAsEof(entry.FirstCluster);
        }

        Span<byte> destination = directorySectors.AsSpan(byteOffset, FatDirectoryEntry.EntrySize);
        FatDirectoryEntryCodec.Write(entry, destination);
    }

    /// <summary>
    /// Writes an entry together with its preceding VFAT LFN slot chain when
    /// <see cref="MutableFatDirectoryEntry.LongName"/> is set; otherwise falls
    /// back to writing the short entry alone. The first free contiguous run of
    /// (slotCount + 1) entries is used, where slotCount is the number of LFN
    /// slots required to spell the long name.
    /// </summary>
    /// <param name="directorySectors">Raw directory bytes.</param>
    /// <param name="entry">Mutable entry to write.</param>
    /// <param name="fatTable">FAT table used for chain validation.</param>
    public void WriteEntryWithLongName(byte[] directorySectors, MutableFatDirectoryEntry entry, FatTable fatTable)
    {
        if (directorySectors == null)
        {
            throw new ArgumentNullException(nameof(directorySectors));
        }

        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        if (fatTable == null)
        {
            throw new ArgumentNullException(nameof(fatTable));
        }

        string longName = entry.LongName ?? string.Empty;
        if (longName.Length == 0)
        {
            int shortSlot = FindNextSlot(directorySectors);
            if (shortSlot < 0)
            {
                throw new InvalidOperationException("Directory has no free slot.");
            }
            WriteEntry(directorySectors, shortSlot, entry, fatTable);
            return;
        }

        LongFileNameCodec codec = new();
        byte checksum = codec.ComputeShortNameChecksum(PackShortName(entry));
        IReadOnlyList<VfatLfnEntry> lfnSlots = codec.EncodeLongName(longName, checksum);
        int requiredSlots = lfnSlots.Count + 1;
        int runStart = FindFreeRun(directorySectors, requiredSlots);
        if (runStart < 0)
        {
            throw new InvalidOperationException("Directory has no contiguous free run for the LFN chain.");
        }

        for (int i = 0; i < lfnSlots.Count; i++)
        {
            int slot = runStart + i;
            int offset = slot * FatDirectoryEntry.EntrySize;
            codec.WriteSlot(lfnSlots[i], directorySectors.AsSpan(offset, FatDirectoryEntry.EntrySize));
        }

        int shortEntrySlot = runStart + lfnSlots.Count;
        WriteEntry(directorySectors, shortEntrySlot, entry, fatTable);
    }

    private static int FindFreeRun(byte[] directorySectors, int requiredSlots)
    {
        int entryCount = directorySectors.Length / FatDirectoryEntry.EntrySize;
        int run = 0;
        int runStart = -1;
        for (int slot = 0; slot < entryCount; slot++)
        {
            int offset = slot * FatDirectoryEntry.EntrySize;
            byte marker = directorySectors[offset];
            bool isFree = marker == FatDirectoryEntry.EndOfDirectory || marker == FatDirectoryEntry.DeletedEntry;
            if (!isFree)
            {
                run = 0;
                runStart = -1;
                continue;
            }
            if (run == 0)
            {
                runStart = slot;
            }
            run++;
            if (run >= requiredSlots)
            {
                return runStart;
            }
        }
        return -1;
    }

    private static byte[] PackShortName(MutableFatDirectoryEntry entry)
    {
        byte[] packed = new byte[11];
        for (int i = 0; i < 11; i++)
        {
            packed[i] = (byte)' ';
        }
        string upperBase = entry.BaseName.ToUpperInvariant();
        string upperExtension = entry.Extension.ToUpperInvariant();
        int baseLength = Math.Min(upperBase.Length, 8);
        int extensionLength = Math.Min(upperExtension.Length, 3);
        Encoding.ASCII.GetBytes(upperBase.AsSpan(0, baseLength), packed.AsSpan(0, baseLength));
        Encoding.ASCII.GetBytes(upperExtension.AsSpan(0, extensionLength), packed.AsSpan(8, extensionLength));
        return packed;
    }
}
