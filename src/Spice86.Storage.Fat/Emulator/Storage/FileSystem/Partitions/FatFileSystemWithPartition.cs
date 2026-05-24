namespace Spice86.Shared.Emulator.Storage.FileSystem.Partitions;

using System;

using Spice86.Shared.Emulator.Storage.FileSystem;

/// <summary>
/// FAT filesystem view bound to a specific MBR partition.
/// </summary>
public sealed class FatFileSystemWithPartition
{
    /// <summary>Partition metadata.</summary>
    public PartitionTableEntry Partition { get; }

    /// <summary>FAT filesystem opened from the partition extent.</summary>
    public FatFileSystem FileSystem { get; }

    /// <summary>
    /// Opens a FAT filesystem using partition LBA offset.
    /// </summary>
    /// <param name="diskImage">Full disk image bytes.</param>
    /// <param name="partition">Partition descriptor.</param>
    public FatFileSystemWithPartition(byte[] diskImage, PartitionTableEntry partition)
    {
        if (diskImage == null)
        {
            throw new ArgumentNullException(nameof(diskImage));
        }

        if (partition == null)
        {
            throw new ArgumentNullException(nameof(partition));
        }

        Partition = partition;

        int startByteOffset = checked((int)(partition.LbaStart * 512u));
        if (startByteOffset < 0 || startByteOffset >= diskImage.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(partition), "Partition start is outside disk image.");
        }

        int maxLength = diskImage.Length - startByteOffset;
        int partitionLengthBytes;
        if (partition.SectorCount == 0)
        {
            partitionLengthBytes = maxLength;
        }
        else
        {
            long requested = (long)partition.SectorCount * 512L;
            partitionLengthBytes = (int)Math.Min(requested, maxLength);
        }

        byte[] partitionBytes = new byte[partitionLengthBytes];
        Array.Copy(diskImage, startByteOffset, partitionBytes, 0, partitionLengthBytes);
        FileSystem = new FatFileSystem(partitionBytes);
    }
}
