namespace Spice86.Core.Emulator.OperatingSystem.FileSystem;

using System;
using System.IO;
using System.Text;

/// <summary>
/// Parses the BIOS Parameter Block (BPB) from the boot sector of a FAT12/FAT16 floppy image.
/// </summary>
public sealed class BiosParameterBlock {
    /// <summary>Gets the number of bytes per sector (typically 512).</summary>
    public ushort BytesPerSector { get; }

    /// <summary>Gets the number of sectors per allocation cluster.</summary>
    public byte SectorsPerCluster { get; }

    /// <summary>Gets the number of reserved sectors at the start of the volume (boot sector count).</summary>
    public ushort ReservedSectors { get; }

    /// <summary>Gets the number of File Allocation Tables (always 1 or 2).</summary>
    public byte NumberOfFats { get; }

    /// <summary>Gets the number of entries in the root directory.</summary>
    public ushort RootDirEntries { get; }

    /// <summary>Gets the total sector count for volumes with fewer than 65536 sectors; 0 if &gt;65535.</summary>
    public ushort TotalSectors16 { get; }

    /// <summary>Gets the media descriptor byte (e.g. 0xF0 for 1.44 MB floppy).</summary>
    public byte MediaDescriptor { get; }

    /// <summary>Gets the number of sectors occupied by one FAT.</summary>
    public ushort SectorsPerFat { get; }

    /// <summary>Gets the number of sectors per track (used for INT 13h geometry).</summary>
    public ushort SectorsPerTrack { get; }

    /// <summary>Gets the number of heads (sides) on the disk.</summary>
    public ushort NumberOfHeads { get; }

    /// <summary>Gets the number of hidden sectors preceding the partition.</summary>
    public uint HiddenSectors { get; }

    /// <summary>Gets the total sector count for volumes with 65536 or more sectors; 0 otherwise.</summary>
    public uint TotalSectors32 { get; }

    /// <summary>Gets the volume label from the extended BPB (may be empty if not present).</summary>
    public string VolumeLabel { get; }

    /// <summary>Gets the total number of sectors on the volume.</summary>
    public int TotalSectors => TotalSectors16 > 0 ? TotalSectors16 : (int)TotalSectors32;

    /// <summary>Gets the logical sector number of the first FAT.</summary>
    public int FatStartSector => ReservedSectors;

    /// <summary>Gets the logical sector number of the root directory.</summary>
    public int RootDirStartSector => ReservedSectors + NumberOfFats * SectorsPerFat;

    /// <summary>Gets the logical sector number of the first data cluster (cluster 2).</summary>
    public int DataStartSector {
        get {
            int rootDirSectors = (RootDirEntries * FatDirectoryEntry.EntrySize + BytesPerSector - 1) / BytesPerSector;
            return RootDirStartSector + rootDirSectors;
        }
    }

    /// <summary>Gets the size of one allocation cluster in bytes.</summary>
    public int BytesPerCluster => BytesPerSector * SectorsPerCluster;

    private BiosParameterBlock(
        ushort bytesPerSector, byte sectorsPerCluster, ushort reservedSectors,
        byte numberOfFats, ushort rootDirEntries, ushort totalSectors16,
        byte mediaDescriptor, ushort sectorsPerFat, ushort sectorsPerTrack,
        ushort numberOfHeads, uint hiddenSectors, uint totalSectors32, string volumeLabel) {
        BytesPerSector = bytesPerSector;
        SectorsPerCluster = sectorsPerCluster;
        ReservedSectors = reservedSectors;
        NumberOfFats = numberOfFats;
        RootDirEntries = rootDirEntries;
        TotalSectors16 = totalSectors16;
        MediaDescriptor = mediaDescriptor;
        SectorsPerFat = sectorsPerFat;
        SectorsPerTrack = sectorsPerTrack;
        NumberOfHeads = numberOfHeads;
        HiddenSectors = hiddenSectors;
        TotalSectors32 = totalSectors32;
        VolumeLabel = volumeLabel;
    }

    /// <summary>
    /// Parses a BPB from the first 512 bytes of a FAT floppy boot sector.
    /// </summary>
    /// <param name="bootSector">The raw boot sector bytes (must be at least 62 bytes).</param>
    /// <returns>The parsed <see cref="BiosParameterBlock"/>.</returns>
    /// <exception cref="InvalidDataException">Thrown when the sector is too short or has a zero bytes-per-sector value.</exception>
    public static BiosParameterBlock Parse(ReadOnlySpan<byte> bootSector) {
        if (bootSector.Length < 62) {
            throw new InvalidDataException($"Boot sector is too short: {bootSector.Length} bytes (expected at least 62).");
        }

        ushort bytesPerSector = BitConverter.ToUInt16(bootSector.Slice(11, 2));
        if (bytesPerSector == 0) {
            throw new InvalidDataException("BPB BytesPerSector is zero; not a valid FAT image.");
        }

        byte sectorsPerCluster = bootSector[13];
        ushort reservedSectors = BitConverter.ToUInt16(bootSector.Slice(14, 2));
        byte numberOfFats = bootSector[16];
        ushort rootDirEntries = BitConverter.ToUInt16(bootSector.Slice(17, 2));
        ushort totalSectors16 = BitConverter.ToUInt16(bootSector.Slice(19, 2));
        byte mediaDescriptor = bootSector[21];
        ushort sectorsPerFat = BitConverter.ToUInt16(bootSector.Slice(22, 2));
        ushort sectorsPerTrack = BitConverter.ToUInt16(bootSector.Slice(24, 2));
        ushort numberOfHeads = BitConverter.ToUInt16(bootSector.Slice(26, 2));
        uint hiddenSectors = BitConverter.ToUInt32(bootSector.Slice(28, 4));
        uint totalSectors32 = BitConverter.ToUInt32(bootSector.Slice(32, 4));

        // Extended BPB volume label (offset 43, 11 bytes) — present when drive number is at offset 36.
        string volumeLabel = string.Empty;
        if (bootSector.Length >= 54 && bootSector[38] == 0x29) {
            volumeLabel = Encoding.ASCII.GetString(bootSector.Slice(43, 11)).TrimEnd();
        }

        return new BiosParameterBlock(
            bytesPerSector, sectorsPerCluster, reservedSectors,
            numberOfFats, rootDirEntries, totalSectors16,
            mediaDescriptor, sectorsPerFat, sectorsPerTrack,
            numberOfHeads, hiddenSectors, totalSectors32, volumeLabel);
    }
}
