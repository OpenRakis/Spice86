namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory.LongFileName;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Reader that walks a raw FAT directory buffer in 32-byte steps and emits
/// <see cref="VfatDirectoryRecord"/> pairs of (optional long name, short entry).
/// </summary>
/// <remarks>
/// VFAT lays out an LFN chain immediately before the 8.3 short entry it
/// describes, with the topmost (last-fragment) slot first. This reader
/// buffers consecutive LFN slots and pairs them with the next non-LFN,
/// non-free entry. A chain is rejected (the short entry is yielded without
/// a long name) when its checksum does not match the 8.3 entry, when slot
/// ordinals are non-contiguous, or when the topmost slot lacks the
/// last-entry flag.
/// </remarks>
public sealed class VfatDirectoryReader {
    /// <summary>Marker byte at offset 0 indicating a deleted directory entry.</summary>
    public const byte DeletedEntryMarker = 0xE5;

    /// <summary>Marker byte at offset 0 indicating the end of the directory.</summary>
    public const byte EndOfDirectoryMarker = 0x00;

    /// <summary>
    /// Enumerates short entries (with optional decoded long names) from a raw
    /// directory buffer. Deleted entries (0xE5) and any LFN slots that
    /// precede them are skipped; the directory terminator (0x00) stops
    /// enumeration.
    /// </summary>
    /// <param name="directoryBytes">Raw directory bytes.</param>
    public static IReadOnlyList<VfatDirectoryRecord> ReadRecords(ReadOnlySpan<byte> directoryBytes) {
        LongFileNameCodec codec = new();
        List<VfatDirectoryRecord> records = new();
        List<VfatLfnEntry> pendingSlots = new();
        int entrySize = FatDirectoryEntry.EntrySize;
        for (int offset = 0; offset + entrySize <= directoryBytes.Length; offset += entrySize) {
            ReadOnlySpan<byte> entry = directoryBytes.Slice(offset, entrySize);
            byte firstByte = entry[0];
            if (firstByte == EndOfDirectoryMarker) {
                break;
            }
            if (firstByte == DeletedEntryMarker) {
                pendingSlots.Clear();
                continue;
            }
            VfatLfnEntry? lfn = LongFileNameCodec.TryParseSlot(entry);
            if (lfn != null) {
                pendingSlots.Add(lfn);
                continue;
            }
            // Skip volume-label entries (attribute bit 0x08) but still drop pending slots.
            if ((entry[11] & 0x08) != 0 && (entry[11] & 0x10) == 0) {
                pendingSlots.Clear();
                continue;
            }
            MutableFatDirectoryEntry shortEntry = MutableFatDirectoryEntry.Parse(entry);
            string longName = TryDecodePendingChain(codec, pendingSlots, shortEntry);
            pendingSlots.Clear();
            records.Add(new VfatDirectoryRecord(shortEntry, longName));
        }
        return records;
    }

    private static string TryDecodePendingChain(
        LongFileNameCodec codec,
        List<VfatLfnEntry> pendingSlots,
        MutableFatDirectoryEntry shortEntry) {
        if (pendingSlots.Count == 0) {
            return string.Empty;
        }
        Span<byte> packedShortName = stackalloc byte[11];
        Span<byte> baseBytes = stackalloc byte[8];
        Span<byte> extensionBytes = stackalloc byte[3];
        baseBytes.Fill((byte)' ');
        extensionBytes.Fill((byte)' ');
        Encoding.ASCII.GetBytes(shortEntry.BaseName.ToUpperInvariant(), baseBytes);
        Encoding.ASCII.GetBytes(shortEntry.Extension.ToUpperInvariant(), extensionBytes);
        baseBytes.CopyTo(packedShortName.Slice(0, 8));
        extensionBytes.CopyTo(packedShortName.Slice(8, 3));
        byte expectedChecksum = LongFileNameCodec.ComputeShortNameChecksum(packedShortName);
        return LongFileNameCodec.DecodeLongName(pendingSlots, expectedChecksum) ?? string.Empty;
    }
}
