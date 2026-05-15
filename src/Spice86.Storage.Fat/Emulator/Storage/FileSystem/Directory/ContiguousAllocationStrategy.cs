namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using System;
using System.Collections.Generic;

using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;

/// <summary>
/// Allocates the first contiguous free run that fits the requested file size.
/// </summary>
public sealed class ContiguousAllocationStrategy : FileAllocationStrategy
{
    private readonly int _bytesPerCluster;

    /// <summary>
    /// Creates a contiguous allocation strategy.
    /// </summary>
    /// <param name="bytesPerCluster">Cluster size in bytes.</param>
    public ContiguousAllocationStrategy(int bytesPerCluster)
    {
        _bytesPerCluster = bytesPerCluster;
    }

    /// <inheritdoc />
    public override IReadOnlyList<uint> Allocate(int fileSizeBytes, FatTable fatTable)
    {
        if (fatTable == null)
        {
            throw new ArgumentNullException(nameof(fatTable));
        }

        int requiredClusters = ComputeRequiredClusterCount(fileSizeBytes, _bytesPerCluster);

        uint runStart = 0;
        int runLength = 0;

        for (uint cluster = 2; cluster < fatTable.ClusterCount; cluster++)
        {
            if (fatTable.IsFree(cluster))
            {
                if (runLength == 0)
                {
                    runStart = cluster;
                }

                runLength++;
                if (runLength == requiredClusters)
                {
                    return AllocateRun(fatTable, runStart, requiredClusters);
                }
            }
            else
            {
                runLength = 0;
            }
        }

        throw new InvalidOperationException("No contiguous free run can satisfy allocation request.");
    }

    private static IReadOnlyList<uint> AllocateRun(FatTable fatTable, uint runStart, int runLength)
    {
        List<uint> clusters = new List<uint>(runLength);

        for (int i = 0; i < runLength; i++)
        {
            uint cluster = runStart + (uint)i;
            clusters.Add(cluster);
        }

        for (int i = 0; i < clusters.Count - 1; i++)
        {
            fatTable.LinkClusters(clusters[i], clusters[i + 1]);
        }

        fatTable.MarkAsEof(clusters[clusters.Count - 1]);
        return clusters;
    }
}
