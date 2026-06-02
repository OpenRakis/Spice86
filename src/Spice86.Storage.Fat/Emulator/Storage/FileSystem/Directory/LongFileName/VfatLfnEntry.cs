namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory.LongFileName;

using System;

/// <summary>
/// Immutable representation of a single 32-byte VFAT Long File Name (LFN) slot
/// as stored in a FAT directory. LFN slots are identified by their attribute
/// byte equal to <c>0x0F</c> (read-only | hidden | system | volume label).
/// </summary>
/// <remarks>
/// Each LFN slot encodes a 13-character UCS-2 little-endian fragment of the
/// full long name across three runs of bytes within the 32-byte entry:
/// offsets 1..10 (5 chars), 14..25 (6 chars), and 28..31 (2 chars). Unused
/// trailing positions are filled with <c>0x0000</c> (NUL terminator) and then
/// <c>0xFFFF</c> padding. The ordinal byte at offset 0 carries the 1-based
/// sequence number; the high bit (<see cref="SlotFlag.LastEntryFlag"/> = 0x40) marks
/// the slot that physically comes first on disc but logically holds the last
/// fragment of the name.
/// </remarks>
public sealed class VfatLfnEntry {
    /// <summary>Byte flags used by VFAT LFN slots.</summary>
    public enum SlotFlag : byte {
        LfnAttribute = 0x0F,
        LastEntryFlag = 0x40
    }

    /// <summary>Maximum UCS-2 characters per LFN slot.</summary>
    public const int CharsPerSlot = 13;

    /// <summary>Gets the 1-based ordinal index (1..20) of this slot.</summary>
    public byte Ordinal { get; }

    /// <summary>Gets a value indicating whether this is the topmost (last) slot.</summary>
    public bool IsLast { get; }

    /// <summary>Gets the 8.3-name checksum that all slots in a chain must share.</summary>
    public byte Checksum { get; }

    /// <summary>
    /// Gets the 13 UCS-2 code units carried by this slot. Unused positions
    /// follow the convention NUL (0x0000) then 0xFFFF padding.
    /// </summary>
    public char[] NameFragment { get; }

    /// <summary>Initialises a new <see cref="VfatLfnEntry"/> with all fields.</summary>
    /// <param name="ordinal">1-based slot ordinal.</param>
    /// <param name="isLast">Whether this is the topmost slot.</param>
    /// <param name="checksum">8.3-name checksum.</param>
    /// <param name="nameFragment">13 UCS-2 code units (defensively copied).</param>
    public VfatLfnEntry(byte ordinal, bool isLast, byte checksum, char[] nameFragment) {
        if (nameFragment.Length != CharsPerSlot) {
            throw new ArgumentException(
                $"LFN slot name fragment must be exactly {CharsPerSlot} chars.",
                nameof(nameFragment));
        }
        Ordinal = ordinal;
        IsLast = isLast;
        Checksum = checksum;
        NameFragment = (char[])nameFragment.Clone();
    }
}
