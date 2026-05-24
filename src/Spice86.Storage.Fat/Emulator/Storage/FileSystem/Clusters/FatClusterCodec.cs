namespace Spice86.Shared.Emulator.Storage.FileSystem.Clusters;

using System;
using System.Buffers.Binary;

using Spice86.Shared.Emulator.Storage.FileSystem;

/// <summary>
/// Reads and writes individual cluster pointer values in a serialised FAT, handling the
/// FAT12 12-bit bit-packing where pairs of entries share two and a half bytes.
/// </summary>
/// <remarks>
/// FAT12: each entry is 12 bits. Even-indexed entries occupy the low nibble of byte n
/// and all of byte n. Odd-indexed entries occupy all of byte n and the high nibble of
/// byte n+1. End-of-chain markers: 0xFF8..0xFFF. Bad cluster: 0xFF7.
/// <para/>
/// FAT16: 16 bits little-endian. EOC: 0xFFF8..0xFFFF. Bad: 0xFFF7.
/// <para/>
/// FAT32: lower 28 bits little-endian (high 4 bits reserved). EOC: 0x0FFFFFF8..0x0FFFFFFF.
/// Bad: 0x0FFFFFF7.
/// </remarks>
public static class FatClusterCodec {
    /// <summary>Minimum cluster value used for end-of-chain marker on FAT12.</summary>
    public const uint Fat12EndOfChainMin = 0xFF8;
    /// <summary>Bad cluster marker on FAT12.</summary>
    public const uint Fat12BadCluster = 0xFF7;
    /// <summary>Minimum cluster value used for end-of-chain marker on FAT16.</summary>
    public const uint Fat16EndOfChainMin = 0xFFF8;
    /// <summary>Bad cluster marker on FAT16.</summary>
    public const uint Fat16BadCluster = 0xFFF7;
    /// <summary>Minimum cluster value used for end-of-chain marker on FAT32 (28-bit space).</summary>
    public const uint Fat32EndOfChainMin = 0x0FFFFFF8;
    /// <summary>Bad cluster marker on FAT32.</summary>
    public const uint Fat32BadCluster = 0x0FFFFFF7;
    /// <summary>FAT32 cluster value mask (high 4 bits reserved).</summary>
    public const uint Fat32ValueMask = 0x0FFFFFFF;

    /// <summary>
    /// Reads the value of cluster <paramref name="clusterIndex"/> from the serialised FAT bytes.
    /// </summary>
    /// <param name="fatBytes">Raw FAT bytes (one FAT, not the whole image).</param>
    /// <param name="clusterIndex">Cluster number (clusters 0 and 1 are reserved).</param>
    /// <param name="fatType">FAT type controlling encoding width.</param>
    /// <returns>Cluster value (truncated to the FAT's bit width).</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="clusterIndex"/> reaches past the end of the FAT.</exception>
    public static uint Read(ReadOnlySpan<byte> fatBytes, uint clusterIndex, FatType fatType) {
        switch (fatType) {
            case FatType.Fat12: {
                    int offset = (int)(clusterIndex + (clusterIndex >> 1));
                    if (offset + 1 >= fatBytes.Length) {
                        throw new ArgumentOutOfRangeException(nameof(clusterIndex), $"Cluster index {clusterIndex} is past end of FAT (length {fatBytes.Length}).");
                    }
                    ushort raw = (ushort)(fatBytes[offset] | (fatBytes[offset + 1] << 8));
                    return (clusterIndex & 1) == 0 ? (uint)(raw & 0x0FFF) : (uint)(raw >> 4);
                }
            case FatType.Fat16: {
                    int offset = (int)(clusterIndex * 2);
                    if (offset + 1 >= fatBytes.Length) {
                        throw new ArgumentOutOfRangeException(nameof(clusterIndex));
                    }
                    return BinaryPrimitives.ReadUInt16LittleEndian(fatBytes.Slice(offset, 2));
                }
            case FatType.Fat32: {
                    int offset = (int)(clusterIndex * 4);
                    if (offset + 3 >= fatBytes.Length) {
                        throw new ArgumentOutOfRangeException(nameof(clusterIndex));
                    }
                    return BinaryPrimitives.ReadUInt32LittleEndian(fatBytes.Slice(offset, 4)) & Fat32ValueMask;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(fatType));
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/> into cluster <paramref name="clusterIndex"/> of the FAT bytes.
    /// On FAT32 the upper 4 reserved bits of the existing entry are preserved.
    /// </summary>
    /// <param name="fatBytes">Mutable FAT bytes.</param>
    /// <param name="clusterIndex">Cluster number to update.</param>
    /// <param name="value">New value (must fit in the FAT's bit width).</param>
    /// <param name="fatType">FAT type controlling encoding width.</param>
    /// <exception cref="ArgumentOutOfRangeException">If the cluster reaches past the end of the FAT or the value does not fit.</exception>
    public static void Write(Span<byte> fatBytes, uint clusterIndex, uint value, FatType fatType) {
        switch (fatType) {
            case FatType.Fat12: {
                    if (value > 0xFFF) {
                        throw new ArgumentOutOfRangeException(nameof(value), $"FAT12 value 0x{value:X} exceeds 12 bits.");
                    }
                    int offset = (int)(clusterIndex + (clusterIndex >> 1));
                    if (offset + 1 >= fatBytes.Length) {
                        throw new ArgumentOutOfRangeException(nameof(clusterIndex), $"Cluster index {clusterIndex} is past end of FAT (length {fatBytes.Length}).");
                    }
                    ushort raw = (ushort)(fatBytes[offset] | (fatBytes[offset + 1] << 8));
                    if ((clusterIndex & 1) == 0) {
                        raw = (ushort)(((uint)raw & 0xF000u) | (value & 0x0FFFu));
                    } else {
                        raw = (ushort)(((uint)raw & 0x000Fu) | ((value & 0x0FFFu) << 4));
                    }
                    fatBytes[offset] = (byte)(raw & 0xFF);
                    fatBytes[offset + 1] = (byte)(raw >> 8);
                    break;
                }
            case FatType.Fat16: {
                    if (value > 0xFFFF) {
                        throw new ArgumentOutOfRangeException(nameof(value), $"FAT16 value 0x{value:X} exceeds 16 bits.");
                    }
                    int offset = (int)(clusterIndex * 2);
                    if (offset + 1 >= fatBytes.Length) {
                        throw new ArgumentOutOfRangeException(nameof(clusterIndex));
                    }
                    BinaryPrimitives.WriteUInt16LittleEndian(fatBytes.Slice(offset, 2), (ushort)value);
                    break;
                }
            case FatType.Fat32: {
                    if ((value & ~Fat32ValueMask) != 0) {
                        throw new ArgumentOutOfRangeException(nameof(value), $"FAT32 value 0x{value:X} exceeds 28 bits.");
                    }
                    int offset = (int)(clusterIndex * 4);
                    if (offset + 3 >= fatBytes.Length) {
                        throw new ArgumentOutOfRangeException(nameof(clusterIndex));
                    }
                    uint existing = BinaryPrimitives.ReadUInt32LittleEndian(fatBytes.Slice(offset, 4));
                    uint merged = (existing & ~Fat32ValueMask) | (value & Fat32ValueMask);
                    BinaryPrimitives.WriteUInt32LittleEndian(fatBytes.Slice(offset, 4), merged);
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(fatType));
        }
    }

    /// <summary>Returns true if <paramref name="value"/> is an end-of-chain marker for <paramref name="fatType"/>.</summary>
    public static bool IsEndOfChain(uint value, FatType fatType) {
        return fatType switch {
            FatType.Fat12 => value >= Fat12EndOfChainMin && value <= 0xFFF,
            FatType.Fat16 => value >= Fat16EndOfChainMin && value <= 0xFFFF,
            FatType.Fat32 => value >= Fat32EndOfChainMin && value <= Fat32ValueMask,
            _ => false
        };
    }

    /// <summary>Returns true if <paramref name="value"/> is the bad-cluster marker for <paramref name="fatType"/>.</summary>
    public static bool IsBadCluster(uint value, FatType fatType) {
        return fatType switch {
            FatType.Fat12 => value == Fat12BadCluster,
            FatType.Fat16 => value == Fat16BadCluster,
            FatType.Fat32 => value == Fat32BadCluster,
            _ => false
        };
    }

    /// <summary>Returns the canonical end-of-chain marker for <paramref name="fatType"/> (0xFFF / 0xFFFF / 0x0FFFFFFF).</summary>
    public static uint EndOfChainMarker(FatType fatType) {
        return fatType switch {
            FatType.Fat12 => 0xFFF,
            FatType.Fat16 => 0xFFFF,
            FatType.Fat32 => Fat32ValueMask,
            _ => throw new ArgumentOutOfRangeException(nameof(fatType))
        };
    }
}
