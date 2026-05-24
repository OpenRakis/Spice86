namespace Spice86.Shared.Emulator.Storage.FileSystem.Clusters;

using System;
using System.Collections.Generic;

using Spice86.Shared.Emulator.Storage.FileSystem;
using Spice86.Shared.Emulator.Storage.FileSystem.BootSector;

/// <summary>
/// Pure observation pass over a <see cref="FatTable"/>. Detects cycles, orphans,
/// out-of-range links and other structural issues without mutating the table.
/// </summary>
public static class FatClusterValidator {
    /// <summary>
    /// Validates that <paramref name="cluster"/> is a usable data cluster index for <paramref name="fatType"/>.
    /// Returns false for reserved indices 0/1 and for indices in the bad/EOC range.
    /// </summary>
    /// <param name="cluster">Cluster index to test.</param>
    /// <param name="fatType">FAT type controlling the reserved value range.</param>
    /// <returns>True if <paramref name="cluster"/> may legally appear as a chain link.</returns>
    public static bool IsValidDataClusterIndex(uint cluster, FatType fatType) {
        if (cluster < 2) {
            return false;
        }
        uint reservedMin = fatType switch {
            FatType.Fat12 => FatClusterCodec.Fat12BadCluster,
            FatType.Fat16 => FatClusterCodec.Fat16BadCluster,
            FatType.Fat32 => FatClusterCodec.Fat32BadCluster,
            _ => throw new ArgumentOutOfRangeException(nameof(fatType))
        };
        return cluster < reservedMin;
    }

    /// <summary>
    /// Inspects the chain starting at <paramref name="start"/> and returns any structural issues found.
    /// </summary>
    /// <param name="table">FAT table to inspect.</param>
    /// <param name="start">First cluster of the chain.</param>
    /// <returns>List of issues (may be empty).</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="table"/> is null.</exception>
    public static IReadOnlyList<BpbValidationIssue> ValidateChain(FatTable table, uint start) {
        if (table is null) {
            throw new ArgumentNullException(nameof(table));
        }
        List<BpbValidationIssue> issues = new();
        try {
            table.FollowChain(start);
        } catch (FatChainCorruptionException ex) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, "Chain", ex.Message));
        } catch (ArgumentOutOfRangeException ex) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, "Chain", ex.Message));
        }
        return issues;
    }

    /// <summary>
    /// Reports every used cluster (non-zero, non-bad) that is not reachable from any
    /// of the supplied chain roots. These are orphaned clusters that should be reclaimed.
    /// </summary>
    /// <param name="table">FAT table to scan.</param>
    /// <param name="chainRoots">First clusters of every known directory or file chain.</param>
    /// <returns>The cluster numbers that are used but unreachable.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="table"/> or <paramref name="chainRoots"/> is null.</exception>
    public static IReadOnlyList<uint> FindOrphanedClusters(FatTable table, IEnumerable<uint> chainRoots) {
        if (table is null) {
            throw new ArgumentNullException(nameof(table));
        }
        if (chainRoots is null) {
            throw new ArgumentNullException(nameof(chainRoots));
        }
        HashSet<uint> reachable = new();
        foreach (uint root in chainRoots) {
            if (root < 2 || !IsValidDataClusterIndex(root, table.FatType)) {
                continue;
            }
            try {
                foreach (uint c in table.FollowChain(root)) {
                    reachable.Add(c);
                }
            } catch (FatChainCorruptionException) {
                // Corrupt chains are not used to mask orphans.
            } catch (ArgumentOutOfRangeException) {
            }
        }
        List<uint> orphans = new();
        for (uint i = 2; i < table.ClusterCount; i++) {
            if (!table.IsFree(i) && !table.IsBad(i) && !reachable.Contains(i)) {
                orphans.Add(i);
            }
        }
        return orphans;
    }
}
