namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Truncated version of BiosParameterBlock returned as part of DosDeviceParameterBlock 
/// </summary>
public class TruncatedBiosParameterBlock : MemoryBasedDataStructure {
    public TruncatedBiosParameterBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the number of bytes per sector.
    /// </summary>
    public ushort BytesPerSector {
        get => UInt16[0x0];
        set => UInt16[0x0] = value;
    }

    /// <summary>
    /// Gets or sets the number of sectors per cluster.
    /// </summary>
    public byte SectorsPerCluster {
        get => UInt8[0x02];
        set => UInt8[0x02] = value;
    }

    /// <summary>
    /// Gets or sets the number of reserved sectors at the start of the disk.
    /// </summary>
    public ushort ReservedSectors {
        get => UInt16[0x03];
        set => UInt16[0x03] = value;
    }

    /// <summary>
    /// Gets or sets the number of File Allocation Tables (FATs).
    /// </summary>
    public byte NumberOfFATs {
        get => UInt8[0x05];
        set => UInt8[0x05] = value;
    }

    /// <summary>
    /// Gets or sets the number of entries in the root directory.
    /// </summary>
    public ushort RootDirectoryEntries {
        get => UInt16[0x06];
        set => UInt16[0x06] = value;
    }

    /// <summary>
    /// Gets or sets the total number of sectors (if less than 65536).
    /// </summary>
    public ushort TotalSectors {
        get => UInt16[0x08];
        set => UInt16[0x08] = value;
    }

    /// <summary>
    /// Gets or sets the media ID byte.
    /// FFh    floppy, double-sided, 8 sectors per track (320K)
    /// FEh    floppy, single-sided, 8 sectors per track (160K)
    /// FDh    floppy, double-sided, 9 sectors per track (360K)
    /// FCh    floppy, single-sided, 9 sectors per track (180K)
    /// </summary>
    public byte MediaId {
        get => UInt8[0x0A];
        set => UInt8[0x0A] = value;
    }

    /// <summary>
    /// Gets or sets the number of sectors per FAT.
    /// </summary>
    public ushort SectorsPerFAT {
        get => UInt16[0x0B];
        set => UInt16[0x0B] = value;
    }

    /// <summary>
    /// Gets or sets the number of sectors per track.
    /// </summary>
    public ushort SectorsPerTrack {
        get => UInt16[0x0D];
        set => UInt16[0x0D] = value;
    }

    /// <summary>
    /// Gets or sets the number of heads.
    /// </summary>
    public ushort NumberOfHeads {
        get => UInt16[0x0F];
        set => UInt16[0x0F] = value;
    }

    /// <summary>
    /// Gets or sets the number of hidden sectors.
    /// </summary>
    public ushort HiddenSectors {
        get => UInt16[0x11];
        set => UInt16[0x11] = value;
    }

    /// <summary>
    /// Gets or sets the total number of sectors if the word at offset 0x08 is zero.
    /// </summary>
    public uint TotalSectorsLarge {
        get => UInt32[0x15];
        set => UInt32[0x15] = value;
    }

    /// <summary>
    /// This field is often unused or part of an extended BPB, but included for completeness.
    /// </summary>
    public ushort NumberOfCylinders {
        get => UInt16[0x1F];
        set => UInt16[0x1F] = value;
    }
}