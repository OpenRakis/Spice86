namespace Spice86.Shared.Emulator.Storage.FileSystem.Partitions;

using System;
using System.Collections.Generic;

/// <summary>
/// Validates MBR partition tables.
/// </summary>
public static class PartitionTableValidator
{
    /// <summary>
    /// Validates partition entries.
    /// </summary>
    /// <param name="mbr">MBR model.</param>
    /// <returns>Validation issues.</returns>
    public static IReadOnlyList<PartitionValidationIssue> ValidatePartitions(MasterBootRecord mbr)
    {
        if (mbr == null)
        {
            throw new ArgumentNullException(nameof(mbr));
        }

        List<PartitionValidationIssue> issues = new List<PartitionValidationIssue>();

        for (int i = 0; i < mbr.Partitions.Count; i++)
        {
            PartitionTableEntry a = mbr.Partitions[i];
            if (!a.IsNonEmpty())
            {
                continue;
            }

            ulong aStart = a.LbaStart;
            ulong aEndExclusive = aStart + a.SectorCount;

            for (int j = i + 1; j < mbr.Partitions.Count; j++)
            {
                PartitionTableEntry b = mbr.Partitions[j];
                if (!b.IsNonEmpty())
                {
                    continue;
                }

                ulong bStart = b.LbaStart;
                ulong bEndExclusive = bStart + b.SectorCount;

                bool overlap = aStart < bEndExclusive && bStart < aEndExclusive;
                if (overlap)
                {
                    issues.Add(new PartitionValidationIssue(PartitionValidationSeverity.Error, "Partition ranges overlap."));
                }
            }
        }

        return issues;
    }
}
