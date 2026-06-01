namespace Spice86.Shared.Emulator.Storage.FileSystem.Partitions;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a parsed Master Boot Record containing up to 4 partition entries.
/// </summary>
public sealed class MasterBootRecord {
    /// <summary>
    /// Offset of partition table in the MBR sector.
    /// </summary>
    public const int PartitionTableOffset = 446;

    /// <summary>
    /// Offset of MBR signature bytes.
    /// </summary>
    public const int SignatureOffset = 510;

    private readonly List<PartitionTableEntry> _partitions;

    /// <summary>
    /// Partition entries (always exactly 4 entries, with empty placeholders as needed).
    /// </summary>
    public IReadOnlyList<PartitionTableEntry> Partitions => _partitions;

    /// <summary>
    /// Creates an MBR from partition entries.
    /// </summary>
    /// <param name="partitions">Partition entries, up to 4.</param>
    public MasterBootRecord(IEnumerable<PartitionTableEntry> partitions) {
        if (partitions == null) {
            throw new ArgumentNullException(nameof(partitions));
        }

        _partitions = partitions.Take(4).ToList();
        while (_partitions.Count < 4) {
            _partitions.Add(new PartitionTableEntry(0x00, 0x00, 0, 0));
        }
    }

    /// <summary>
    /// Finds the first bootable partition.
    /// </summary>
    /// <returns>Bootable partition, or null when none exists.</returns>
    public PartitionTableEntry? FindBootablePartition() {
        for (int i = 0; i < _partitions.Count; i++) {
            if (_partitions[i].IsBootable()) {
                return _partitions[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first non-empty partition.
    /// </summary>
    /// <returns>First non-empty partition, or null when none exists.</returns>
    public PartitionTableEntry? FindFirstNonEmptyPartition() {
        for (int i = 0; i < _partitions.Count; i++) {
            if (_partitions[i].IsNonEmpty()) {
                return _partitions[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true when signature bytes are valid in the supplied MBR sector.
    /// </summary>
    /// <param name="mbrSector">Raw sector bytes.</param>
    /// <returns>True when signature is 0x55AA.</returns>
    public static bool ValidateMagic(ReadOnlySpan<byte> mbrSector) {
        if (mbrSector.Length < 512) {
            return false;
        }

        return mbrSector[SignatureOffset] == 0x55 && mbrSector[SignatureOffset + 1] == 0xAA;
    }
}
