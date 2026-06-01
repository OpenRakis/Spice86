namespace Spice86.Shared.Emulator.Storage.FileSystem.Partitions;

using System;
using System.Collections.Generic;
using System.IO;

using Spice86.Shared.Emulator.Storage.FileSystem;

/// <summary>
/// Opens FAT disk images either as raw FAT volumes or MBR-partitioned disks.
/// </summary>
public sealed class FatDiskImage
{
    private readonly List<FatFileSystemWithPartition> _partitions;

    /// <summary>
    /// Partitioned FAT volumes discovered in the disk image.
    /// </summary>
    public IReadOnlyList<FatFileSystemWithPartition> Partitions => _partitions;

    private FatDiskImage(List<FatFileSystemWithPartition> partitions)
    {
        _partitions = partitions;
    }

    /// <summary>
    /// Opens a disk image as FAT.
    /// </summary>
    /// <param name="diskImage">Disk image bytes.</param>
    /// <returns>Opened FAT disk image model.</returns>
    public static FatDiskImage Open(byte[] diskImage)
    {
        if (diskImage == null)
        {
            throw new ArgumentNullException(nameof(diskImage));
        }

        List<FatFileSystemWithPartition> filesystems = new List<FatFileSystemWithPartition>();

        if (TryParseMbr(diskImage, out MasterBootRecord? mbr) && mbr != null)
        {
            for (int i = 0; i < mbr.Partitions.Count; i++)
            {
                PartitionTableEntry partition = mbr.Partitions[i];
                if (!partition.IsNonEmpty())
                {
                    continue;
                }

                try
                {
                    filesystems.Add(new FatFileSystemWithPartition(diskImage, partition));
                }
                catch (InvalidDataException)
                {
                    // Keep scanning remaining partitions; non-FAT partitions are expected in mixed disks.
                }
            }
        }

        if (filesystems.Count == 0)
        {
            PartitionTableEntry syntheticPartition = new PartitionTableEntry(0x00, 0x00, 0, (uint)(diskImage.Length / 512));
            filesystems.Add(new FatFileSystemWithPartition(diskImage, syntheticPartition));
        }

        return new FatDiskImage(filesystems);
    }

    /// <summary>
    /// Returns the preferred filesystem for booting: active partition first, then first non-empty.
    /// </summary>
    /// <returns>Selected FAT filesystem.</returns>
    public FatFileSystem GetBootableFilesystem()
    {
        for (int i = 0; i < _partitions.Count; i++)
        {
            if (_partitions[i].Partition.IsBootable())
            {
                return _partitions[i].FileSystem;
            }
        }

        for (int i = 0; i < _partitions.Count; i++)
        {
            if (_partitions[i].Partition.IsNonEmpty())
            {
                return _partitions[i].FileSystem;
            }
        }

        return _partitions[0].FileSystem;
    }

    private static bool TryParseMbr(byte[] diskImage, out MasterBootRecord? mbr)
    {
        mbr = null;

        if (diskImage.Length < 512)
        {
            return false;
        }

        try
        {
            mbr = MbrCodec.Parse(diskImage.AsSpan(0, 512));
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
