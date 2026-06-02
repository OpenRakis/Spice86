namespace Spice86.Shared.Emulator.Storage.FileSystem.BootSector;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

/// <summary>
/// Writable mirror of <see cref="FatBiosParameterBlock"/> used for creating or editing FAT boot sectors.
/// All offsets in property documentation are byte offsets inside the 512 byte boot sector,
/// matching the FAT specification and the values used by dosbox-staging's
/// <c>drive_fat.cpp</c> boot sector struct.
/// </summary>
public sealed class MutableBiosParameterBlock {
    /// <summary>Offset 11. Bytes per logical sector. Common value: 512.</summary>
    public ushort BytesPerSector { get; set; } = 512;

    /// <summary>Offset 13. Sectors per allocation cluster. Must be a power of two.</summary>
    public byte SectorsPerCluster { get; set; } = 1;

    /// <summary>Offset 14. Number of reserved sectors before the first FAT. FAT12/16: 1. FAT32: typically 32.</summary>
    public ushort ReservedSectors { get; set; } = 1;

    /// <summary>Offset 16. Number of File Allocation Tables. Almost always 2.</summary>
    public byte NumberOfFats { get; set; } = 2;

    /// <summary>Offset 17. Number of root directory entries. FAT32: 0.</summary>
    public ushort RootDirEntries { get; set; }

    /// <summary>Offset 19. Total sector count when fits in 16 bits, otherwise 0.</summary>
    public ushort TotalSectors16 { get; set; }

    /// <summary>Offset 21. Media descriptor byte. 0xF0 for 1.44 MB floppy, 0xF8 for hard disk.</summary>
    public byte MediaDescriptor { get; set; } = 0xF8;

    /// <summary>Offset 22. Sectors per FAT (FAT12/16). 0 on FAT32; use <see cref="SectorsPerFat32"/> instead.</summary>
    public ushort SectorsPerFat { get; set; }

    /// <summary>Offset 24. Sectors per track. INT 13h CHS geometry.</summary>
    public ushort SectorsPerTrack { get; set; }

    /// <summary>Offset 26. Number of heads. INT 13h CHS geometry.</summary>
    public ushort NumberOfHeads { get; set; }

    /// <summary>Offset 28. Hidden sectors before this partition (LBA_Start in the MBR).</summary>
    public uint HiddenSectors { get; set; }

    /// <summary>Offset 32. Total sector count when it does not fit in 16 bits.</summary>
    public uint TotalSectors32 { get; set; }

    /// <summary>Offset 36 (FAT32 only). Sectors per FAT for FAT32 volumes.</summary>
    public uint SectorsPerFat32 { get; set; }

    /// <summary>Offset 44 (FAT32 only). Cluster number of the root directory.</summary>
    public uint RootCluster { get; set; } = 2;

    /// <summary>
    /// Volume label string (11 ASCII chars, space-padded). On FAT12/16 written at offset 43;
    /// on FAT32 written at offset 71.
    /// </summary>
    public string VolumeLabel { get; set; } = "NO NAME    ";

    /// <summary>
    /// Boot signature (extended BPB indicator). Equals 0x29 when the extended BPB
    /// (label + system id + serial number) is present. Set to 0x00 to omit it.
    /// </summary>
    public byte ExtendedBootSignature { get; set; } = 0x29;

    /// <summary>
    /// Creates a deep copy that shares no mutable state with this instance.
    /// </summary>
    /// <returns>A new independent <see cref="MutableBiosParameterBlock"/> with identical field values.</returns>
    public MutableBiosParameterBlock Clone() {
        return new MutableBiosParameterBlock {
            BytesPerSector = BytesPerSector,
            SectorsPerCluster = SectorsPerCluster,
            ReservedSectors = ReservedSectors,
            NumberOfFats = NumberOfFats,
            RootDirEntries = RootDirEntries,
            TotalSectors16 = TotalSectors16,
            MediaDescriptor = MediaDescriptor,
            SectorsPerFat = SectorsPerFat,
            SectorsPerTrack = SectorsPerTrack,
            NumberOfHeads = NumberOfHeads,
            HiddenSectors = HiddenSectors,
            TotalSectors32 = TotalSectors32,
            SectorsPerFat32 = SectorsPerFat32,
            RootCluster = RootCluster,
            VolumeLabel = VolumeLabel,
            ExtendedBootSignature = ExtendedBootSignature
        };
    }

    /// <summary>
    /// Serialises this BPB into the supplied boot sector buffer at the FAT-specification offsets.
    /// The buffer must be at least 90 bytes for FAT32 or 62 bytes for FAT12/16.
    /// This method writes BPB fields only; it does not touch the boot code (offset 0..2 and 62..509)
    /// nor the boot signature at 510-511 (use <see cref="FatBootSectorCodec.Write"/> for that).
    /// </summary>
    /// <param name="bootSector">Destination buffer.</param>
    /// <param name="fatType">FAT type to serialise as; controls whether FAT32-specific fields are written.</param>
    /// <exception cref="ArgumentException">If the buffer is too short.</exception>
    public void Serialize(Span<byte> bootSector, FatType fatType) {
        int minLen = fatType == FatType.Fat32 ? 90 : 62;
        if (bootSector.Length < minLen) {
            throw new ArgumentException($"Boot sector buffer is too short: {bootSector.Length} bytes (need at least {minLen} for {fatType}).", nameof(bootSector));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.Slice(11, 2), BytesPerSector);
        bootSector[13] = SectorsPerCluster;
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.Slice(14, 2), ReservedSectors);
        bootSector[16] = NumberOfFats;
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.Slice(17, 2), RootDirEntries);
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.Slice(19, 2), TotalSectors16);
        bootSector[21] = MediaDescriptor;
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.Slice(22, 2), SectorsPerFat);
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.Slice(24, 2), SectorsPerTrack);
        BinaryPrimitives.WriteUInt16LittleEndian(bootSector.Slice(26, 2), NumberOfHeads);
        BinaryPrimitives.WriteUInt32LittleEndian(bootSector.Slice(28, 4), HiddenSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(bootSector.Slice(32, 4), TotalSectors32);

        if (fatType == FatType.Fat32) {
            BinaryPrimitives.WriteUInt32LittleEndian(bootSector.Slice(36, 4), SectorsPerFat32);
            BinaryPrimitives.WriteUInt16LittleEndian(bootSector.Slice(40, 2), 0);  // ExtFlags.
            BinaryPrimitives.WriteUInt16LittleEndian(bootSector.Slice(42, 2), 0);  // FsVersion.
            BinaryPrimitives.WriteUInt32LittleEndian(bootSector.Slice(44, 4), RootCluster);
            bootSector[66] = ExtendedBootSignature;
            if (ExtendedBootSignature == 0x29) {
                WriteLabel(bootSector.Slice(71, 11));
                Encoding.ASCII.GetBytes("FAT32   ", bootSector.Slice(82, 8));
            }
        } else {
            bootSector[38] = ExtendedBootSignature;
            if (ExtendedBootSignature == 0x29) {
                WriteLabel(bootSector.Slice(43, 11));
                string fsId = fatType == FatType.Fat12 ? "FAT12   " : "FAT16   ";
                Encoding.ASCII.GetBytes(fsId, bootSector.Slice(54, 8));
            }
        }
    }

    private void WriteLabel(Span<byte> dst) {
        dst.Fill((byte)' ');
        string label = VolumeLabel ?? string.Empty;
        if (label.Length > 11) {
            label = label[..11];
        }
        Encoding.ASCII.GetBytes(label, dst);
    }

    /// <summary>
    /// Creates a mutable copy from a parsed read-only <see cref="FatBiosParameterBlock"/>.
    /// </summary>
    /// <param name="source">Source BPB to mirror.</param>
    /// <returns>A new <see cref="MutableBiosParameterBlock"/> seeded with the source values.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="source"/> is null.</exception>
    public static MutableBiosParameterBlock FromReadOnly(FatBiosParameterBlock source) {
        if (source is null) {
            throw new ArgumentNullException(nameof(source));
        }
        return new MutableBiosParameterBlock {
            BytesPerSector = source.BytesPerSector,
            SectorsPerCluster = source.SectorsPerCluster,
            ReservedSectors = source.ReservedSectors,
            NumberOfFats = source.NumberOfFats,
            RootDirEntries = source.RootDirEntries,
            TotalSectors16 = source.TotalSectors16,
            MediaDescriptor = source.MediaDescriptor,
            SectorsPerFat = source.SectorsPerFat,
            SectorsPerTrack = source.SectorsPerTrack,
            NumberOfHeads = source.NumberOfHeads,
            HiddenSectors = source.HiddenSectors,
            TotalSectors32 = source.TotalSectors32,
            SectorsPerFat32 = source.SectorsPerFat32,
            RootCluster = source.RootCluster,
            VolumeLabel = string.IsNullOrEmpty(source.VolumeLabel) ? "NO NAME    " : source.VolumeLabel.PadRight(11)
        };
    }
}
