namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory.LongFileName;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

/// <summary>
/// Codec for VFAT Long File Name (LFN) entries: computes the 8.3-name
/// checksum, encodes a long name into one or more <see cref="VfatLfnEntry"/>
/// slots, parses a 32-byte directory record into a slot, and reconstructs the
/// long name from an ordered chain of slots.
/// </summary>
public sealed class LongFileNameCodec {
    /// <summary>
    /// Computes the VFAT checksum over an 11-byte 8.3 short name. The DOS
    /// short name must be padded with spaces (0x20) to exactly 11 bytes
    /// (8 base + 3 extension, no dot) before being passed here.
    /// </summary>
    /// <param name="shortName11">The 11-byte padded DOS short name.</param>
    /// <returns>The checksum byte stored in every LFN slot of the chain.</returns>
    public static byte ComputeShortNameChecksum(ReadOnlySpan<byte> shortName11) {
        if (shortName11.Length != 11) {
            throw new ArgumentException(
                "Short-name checksum input must be exactly 11 bytes.",
                nameof(shortName11));
        }
        byte sum = 0;
        for (int i = 0; i < 11; i++) {
            sum = (byte)((((sum & 1) << 7) | (sum >> 1)) + shortName11[i]);
        }
        return sum;
    }

    /// <summary>
    /// Encodes a long name into the sequence of LFN slots that must precede
    /// its 8.3 entry on disc. The returned list is ordered with the topmost
    /// (last-fragment) slot first, matching disc layout.
    /// </summary>
    /// <param name="longName">The user-visible long file name.</param>
    /// <param name="shortNameChecksum">Checksum of the paired 8.3 entry.</param>
    /// <returns>Ordered LFN slots, topmost (last fragment) first.</returns>
    public static IReadOnlyList<VfatLfnEntry> EncodeLongName(string longName, byte shortNameChecksum) {
        if (longName.Length == 0) {
            return Array.Empty<VfatLfnEntry>();
        }
        int slotCount = (longName.Length + VfatLfnEntry.CharsPerSlot - 1) / VfatLfnEntry.CharsPerSlot;
        VfatLfnEntry[] slots = new VfatLfnEntry[slotCount];
        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++) {
            char[] fragment = new char[VfatLfnEntry.CharsPerSlot];
            int sourceOffset = slotIndex * VfatLfnEntry.CharsPerSlot;
            int charsRemaining = longName.Length - sourceOffset;
            int copyLength = Math.Min(VfatLfnEntry.CharsPerSlot, charsRemaining);
            for (int j = 0; j < copyLength; j++) {
                fragment[j] = longName[sourceOffset + j];
            }
            if (copyLength < VfatLfnEntry.CharsPerSlot) {
                fragment[copyLength] = '\u0000';
                for (int k = copyLength + 1; k < VfatLfnEntry.CharsPerSlot; k++) {
                    fragment[k] = '\uFFFF';
                }
            }
            bool isLast = slotIndex == slotCount - 1;
            byte ordinal = (byte)(slotIndex + 1);
            VfatLfnEntry slot = new VfatLfnEntry(ordinal, isLast, shortNameChecksum, fragment);
            // Topmost (last-fragment) slot appears first on disc.
            slots[slotCount - 1 - slotIndex] = slot;
        }
        return slots;
    }

    /// <summary>
    /// Serialises a single LFN slot into a 32-byte directory record.
    /// </summary>
    /// <param name="slot">The slot to serialise.</param>
    /// <param name="destination">32-byte destination span.</param>
    public static void WriteSlot(VfatLfnEntry slot, Span<byte> destination) {
        if (destination.Length < FatDirectoryEntry.EntrySize) {
            throw new ArgumentException(
                $"LFN slot destination must be at least {FatDirectoryEntry.EntrySize} bytes.",
                nameof(destination));
        }
        destination.Slice(0, FatDirectoryEntry.EntrySize).Clear();
        byte ordinalByte = slot.Ordinal;
        if (slot.IsLast) {
            ordinalByte = (byte)(ordinalByte | (byte)VfatLfnEntry.SlotFlag.LastEntryFlag);
        }
        destination[0] = ordinalByte;
        WriteUcs2Chars(slot.NameFragment.AsSpan(0, 5), destination.Slice(1, 10));
        destination[11] = (byte)VfatLfnEntry.SlotFlag.LfnAttribute;
        destination[12] = 0;
        destination[13] = slot.Checksum;
        WriteUcs2Chars(slot.NameFragment.AsSpan(5, 6), destination.Slice(14, 12));
        destination[26] = 0;
        destination[27] = 0;
        WriteUcs2Chars(slot.NameFragment.AsSpan(11, 2), destination.Slice(28, 4));
    }

    /// <summary>
    /// Parses a 32-byte directory record as an LFN slot. Returns null when
    /// the record is not an LFN slot (attribute byte != 0x0F).
    /// </summary>
    /// <param name="entryBytes">32-byte directory record.</param>
    public static VfatLfnEntry? TryParseSlot(ReadOnlySpan<byte> entryBytes) {
        if (entryBytes.Length < FatDirectoryEntry.EntrySize) {
            return null;
        }
        if (entryBytes[11] != (byte)VfatLfnEntry.SlotFlag.LfnAttribute) {
            return null;
        }
        byte ordinalByte = entryBytes[0];
        bool isLast = (ordinalByte & (byte)VfatLfnEntry.SlotFlag.LastEntryFlag) != 0;
        byte ordinal = (byte)(ordinalByte & 0x1F);
        byte checksum = entryBytes[13];
        char[] fragment = new char[VfatLfnEntry.CharsPerSlot];
        ReadUcs2Chars(entryBytes.Slice(1, 10), fragment.AsSpan(0, 5));
        ReadUcs2Chars(entryBytes.Slice(14, 12), fragment.AsSpan(5, 6));
        ReadUcs2Chars(entryBytes.Slice(28, 4), fragment.AsSpan(11, 2));
        return new VfatLfnEntry(ordinal, isLast, checksum, fragment);
    }

    /// <summary>
    /// Reconstructs the long name from an ordered chain of slots as they
    /// appear on disc (topmost / last-fragment first). Returns null when the
    /// chain is malformed: missing last flag, non-contiguous ordinals,
    /// inconsistent checksum, or expected-checksum mismatch.
    /// </summary>
    /// <param name="slotsInDiscOrder">Slots in on-disc order.</param>
    /// <param name="expectedChecksum">Checksum of the paired 8.3 entry.</param>
    public static string? DecodeLongName(IReadOnlyList<VfatLfnEntry> slotsInDiscOrder, byte expectedChecksum) {
        if (slotsInDiscOrder.Count == 0) {
            return null;
        }
        VfatLfnEntry first = slotsInDiscOrder[0];
        if (!first.IsLast) {
            return null;
        }
        byte expectedOrdinal = first.Ordinal;
        if (expectedOrdinal != slotsInDiscOrder.Count) {
            return null;
        }
        if (first.Checksum != expectedChecksum) {
            return null;
        }
        char[] reassembled = new char[slotsInDiscOrder.Count * VfatLfnEntry.CharsPerSlot];
        for (int i = 0; i < slotsInDiscOrder.Count; i++) {
            VfatLfnEntry slot = slotsInDiscOrder[i];
            byte expectedThisOrdinal = (byte)(expectedOrdinal - i);
            if (slot.Ordinal != expectedThisOrdinal) {
                return null;
            }
            if (slot.Checksum != expectedChecksum) {
                return null;
            }
            int destSlotIndex = expectedThisOrdinal - 1;
            slot.NameFragment.AsSpan().CopyTo(reassembled.AsSpan(destSlotIndex * VfatLfnEntry.CharsPerSlot));
        }
        int terminatorIndex = Array.IndexOf(reassembled, '\u0000');
        int effectiveLength = terminatorIndex < 0 ? reassembled.Length : terminatorIndex;
        return new string(reassembled, 0, effectiveLength);
    }

    private static void WriteUcs2Chars(ReadOnlySpan<char> chars, Span<byte> destination) {
        for (int i = 0; i < chars.Length; i++) {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(i * 2, 2), chars[i]);
        }
    }

    private static void ReadUcs2Chars(ReadOnlySpan<byte> source, Span<char> destination) {
        for (int i = 0; i < destination.Length; i++) {
            destination[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(i * 2, 2));
        }
    }
}
