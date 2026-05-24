namespace Spice86.Shared.Emulator.Storage.FileSystem.Partitions;

/// <summary>
/// Represents one 16-byte partition table entry from an MBR sector.
/// </summary>
public sealed class PartitionTableEntry
{
    /// <summary>Boot flag byte. 0x80 marks active partition.</summary>
    public byte BootIndicator { get; }

    /// <summary>Partition type byte.</summary>
    public byte PartitionType { get; }

    /// <summary>Partition start LBA sector.</summary>
    public uint LbaStart { get; }

    /// <summary>Partition sector count.</summary>
    public uint SectorCount { get; }

    /// <summary>
    /// Creates a partition entry.
    /// </summary>
    /// <param name="bootIndicator">Boot flag (0x80 active, 0x00 inactive).</param>
    /// <param name="partitionType">Partition type identifier.</param>
    /// <param name="lbaStart">LBA start sector.</param>
    /// <param name="sectorCount">Partition size in sectors.</param>
    public PartitionTableEntry(byte bootIndicator, byte partitionType, uint lbaStart, uint sectorCount)
    {
        BootIndicator = bootIndicator;
        PartitionType = partitionType;
        LbaStart = lbaStart;
        SectorCount = sectorCount;
    }

    /// <summary>
    /// Returns true when the entry has non-zero type and non-zero size.
    /// </summary>
    public bool IsNonEmpty()
    {
        return PartitionType != 0 && SectorCount != 0;
    }

    /// <summary>
    /// Returns true when the entry is marked bootable.
    /// </summary>
    public bool IsBootable()
    {
        return BootIndicator == 0x80 && IsNonEmpty();
    }
}
