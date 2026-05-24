namespace Spice86.Shared.Emulator.Storage.FileSystem.Partitions;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Parses and writes MBR sectors.
/// </summary>
public static class MbrCodec
{
    private const int PartitionEntrySize = 16;

    /// <summary>
    /// Parses an MBR sector.
    /// </summary>
    /// <param name="mbrSector">Raw MBR sector bytes.</param>
    /// <returns>Parsed MBR model.</returns>
    public static MasterBootRecord Parse(ReadOnlySpan<byte> mbrSector)
    {
        if (mbrSector.Length < 512)
        {
            throw new InvalidDataException("MBR sector must be at least 512 bytes.");
        }

        if (mbrSector[MasterBootRecord.SignatureOffset] != 0x55 || mbrSector[MasterBootRecord.SignatureOffset + 1] != 0xAA)
        {
            throw new InvalidDataException("MBR signature is invalid.");
        }

        List<PartitionTableEntry> partitions = new List<PartitionTableEntry>(4);
        for (int i = 0; i < 4; i++)
        {
            int offset = MasterBootRecord.PartitionTableOffset + i * PartitionEntrySize;
            byte bootIndicator = mbrSector[offset];
            byte partitionType = mbrSector[offset + 4];
            uint lbaStart = BinaryPrimitives.ReadUInt32LittleEndian(mbrSector.Slice(offset + 8, 4));
            uint sectorCount = BinaryPrimitives.ReadUInt32LittleEndian(mbrSector.Slice(offset + 12, 4));
            partitions.Add(new PartitionTableEntry(bootIndicator, partitionType, lbaStart, sectorCount));
        }

        return new MasterBootRecord(partitions);
    }

    /// <summary>
    /// Parses an MBR sector from a byte array.
    /// </summary>
    /// <param name="mbrSector">Raw MBR sector bytes.</param>
    /// <returns>Parsed MBR model.</returns>
    public static MasterBootRecord Parse(byte[] mbrSector)
    {
        if (mbrSector == null)
        {
            throw new ArgumentNullException(nameof(mbrSector));
        }

        return Parse(mbrSector.AsSpan());
    }

    /// <summary>
    /// Writes an MBR sector.
    /// </summary>
    /// <param name="mbr">MBR model.</param>
    /// <param name="destination">Destination sector bytes.</param>
    public static void Write(MasterBootRecord mbr, Span<byte> destination)
    {
        if (mbr == null)
        {
            throw new ArgumentNullException(nameof(mbr));
        }

        if (destination.Length < 512)
        {
            throw new ArgumentException("Destination must be at least 512 bytes.", nameof(destination));
        }

        destination.Slice(0, 512).Clear();

        for (int i = 0; i < 4; i++)
        {
            PartitionTableEntry entry = mbr.Partitions[i];
            int offset = MasterBootRecord.PartitionTableOffset + i * PartitionEntrySize;
            destination[offset] = entry.BootIndicator;
            destination[offset + 4] = entry.PartitionType;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 8, 4), entry.LbaStart);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 12, 4), entry.SectorCount);
        }

        destination[MasterBootRecord.SignatureOffset] = 0x55;
        destination[MasterBootRecord.SignatureOffset + 1] = 0xAA;
    }
}
