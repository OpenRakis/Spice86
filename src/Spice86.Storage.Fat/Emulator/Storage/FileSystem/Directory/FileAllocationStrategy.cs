namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using System;
using System.Collections.Generic;

using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;

/// <summary>
/// Base type for FAT file cluster allocation strategies.
/// </summary>
public abstract class FileAllocationStrategy
{
    /// <summary>
    /// Allocates a FAT cluster chain for a file size.
    /// </summary>
    /// <param name="fileSizeBytes">File size in bytes.</param>
    /// <param name="fatTable">Mutable FAT table.</param>
    /// <returns>Ordered list of allocated clusters.</returns>
    public abstract IReadOnlyList<uint> Allocate(int fileSizeBytes, FatTable fatTable);

    /// <summary>
    /// Computes clusters required for a file size.
    /// </summary>
    /// <param name="fileSizeBytes">File size in bytes.</param>
    /// <param name="bytesPerCluster">Bytes per cluster.</param>
    /// <returns>Required cluster count.</returns>
    protected static int ComputeRequiredClusterCount(int fileSizeBytes, int bytesPerCluster)
    {
        if (fileSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));
        }

        if (bytesPerCluster <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerCluster));
        }

        if (fileSizeBytes == 0)
        {
            return 1;
        }

        return (fileSizeBytes + bytesPerCluster - 1) / bytesPerCluster;
    }
}
