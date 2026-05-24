namespace Spice86.Shared.Emulator.Storage.FileSystem.BootSector;

using System;
using System.Collections.Generic;

/// <summary>
/// Checks a <see cref="MutableBiosParameterBlock"/> for internal consistency against a target
/// <see cref="FatType"/>. The validator never mutates the BPB - it only reports issues.
/// </summary>
public static class FatBootSectorValidator {
    /// <summary>
    /// Returns the list of consistency issues found in <paramref name="bpb"/> when interpreted as <paramref name="fatType"/>.
    /// An empty list means the BPB is internally consistent (this does not guarantee the volume is mountable).
    /// </summary>
    /// <param name="bpb">BPB to inspect.</param>
    /// <param name="fatType">Target FAT type.</param>
    /// <returns>List of issues, possibly empty.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="bpb"/> is null.</exception>
    public static IReadOnlyList<BpbValidationIssue> ValidateBpbConsistency(MutableBiosParameterBlock bpb, FatType fatType) {
        if (bpb is null) {
            throw new ArgumentNullException(nameof(bpb));
        }

        List<BpbValidationIssue> issues = new();

        if (bpb.BytesPerSector == 0) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.BytesPerSector), "BytesPerSector is 0."));
        } else if (!IsPowerOfTwo(bpb.BytesPerSector) || bpb.BytesPerSector < 512 || bpb.BytesPerSector > 4096) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Warning, nameof(bpb.BytesPerSector), $"BytesPerSector {bpb.BytesPerSector} is not a standard value (512/1024/2048/4096)."));
        }

        if (bpb.SectorsPerCluster == 0) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.SectorsPerCluster), "SectorsPerCluster is 0."));
        } else if (!IsPowerOfTwo(bpb.SectorsPerCluster)) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.SectorsPerCluster), $"SectorsPerCluster {bpb.SectorsPerCluster} is not a power of two."));
        }

        if (bpb.ReservedSectors == 0) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.ReservedSectors), "ReservedSectors is 0; expected at least 1 for the boot sector."));
        }

        if (bpb.NumberOfFats == 0) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.NumberOfFats), "NumberOfFats is 0; expected 1 or 2."));
        }

        bool totals16NonZero = bpb.TotalSectors16 != 0;
        bool totals32NonZero = bpb.TotalSectors32 != 0;
        if (totals16NonZero && totals32NonZero) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.TotalSectors16), $"TotalSectors16={bpb.TotalSectors16} and TotalSectors32={bpb.TotalSectors32} are both non-zero; exactly one must be set."));
        }
        if (!totals16NonZero && !totals32NonZero) {
            issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.TotalSectors16), "Both TotalSectors16 and TotalSectors32 are zero; one must be set."));
        }

        switch (fatType) {
            case FatType.Fat32:
                if (bpb.SectorsPerFat != 0) {
                    issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.SectorsPerFat), $"SectorsPerFat (offset 22) must be 0 on FAT32; got {bpb.SectorsPerFat}."));
                }
                if (bpb.SectorsPerFat32 == 0) {
                    issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.SectorsPerFat32), "SectorsPerFat32 (offset 36) must be non-zero on FAT32."));
                }
                if (bpb.RootDirEntries != 0) {
                    issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.RootDirEntries), $"RootDirEntries must be 0 on FAT32; got {bpb.RootDirEntries}."));
                }
                if (bpb.RootCluster < 2) {
                    issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.RootCluster), $"RootCluster {bpb.RootCluster} is invalid; data clusters start at 2."));
                }
                if (!totals32NonZero) {
                    issues.Add(new BpbValidationIssue(BpbValidationSeverity.Warning, nameof(bpb.TotalSectors32), "FAT32 volumes usually report total sector count via TotalSectors32."));
                }
                break;
            case FatType.Fat12:
            case FatType.Fat16:
                if (bpb.SectorsPerFat == 0) {
                    issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.SectorsPerFat), $"SectorsPerFat (offset 22) must be non-zero on {fatType}."));
                }
                if (bpb.RootDirEntries == 0) {
                    issues.Add(new BpbValidationIssue(BpbValidationSeverity.Error, nameof(bpb.RootDirEntries), $"RootDirEntries must be non-zero on {fatType}."));
                }
                break;
        }

        return issues;
    }

    private static bool IsPowerOfTwo(int value) {
        return value > 0 && (value & (value - 1)) == 0;
    }
}
