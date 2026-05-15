namespace Spice86.Shared.Emulator.Storage.FileSystem.Directory;

using System;
using System.Collections.Generic;

using Spice86.Shared.Emulator.Storage.FileSystem.Clusters;

/// <summary>
/// Allocates the first free clusters available, regardless of contiguity.
/// </summary>
public sealed class FirstFitAllocationStrategy : FileAllocationStrategy {
    private readonly int _bytesPerCluster;

    /// <summary>
    /// Creates a first-fit allocation strategy.
    /// </summary>
    /// <param name="bytesPerCluster">Cluster size in bytes.</param>
    public FirstFitAllocationStrategy(int bytesPerCluster) {
        _bytesPerCluster = bytesPerCluster;
    }

    /// <inheritdoc />
    public override IReadOnlyList<uint> Allocate(int fileSizeBytes, FatTable fatTable) {
        if (fatTable == null) {
            throw new ArgumentNullException(nameof(fatTable));
        }

        int requiredClusters = ComputeRequiredClusterCount(fileSizeBytes, _bytesPerCluster);
        List<uint> clusters = new List<uint>(requiredClusters);

        for (uint cluster = 2; cluster < fatTable.ClusterCount && clusters.Count < requiredClusters; cluster++) {
            if (fatTable.IsFree(cluster)) {
                clusters.Add(cluster);
            }
        }

        if (clusters.Count != requiredClusters) {
            throw new InvalidOperationException("No sufficient free clusters available for first-fit allocation.");
        }

        for (int i = 0; i < clusters.Count - 1; i++) {
            fatTable.LinkClusters(clusters[i], clusters[i + 1]);
        }

        fatTable.MarkAsEof(clusters[clusters.Count - 1]);
        return clusters;
    }
}
