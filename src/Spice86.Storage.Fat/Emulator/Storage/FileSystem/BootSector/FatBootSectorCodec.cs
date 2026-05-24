namespace Spice86.Shared.Emulator.Storage.FileSystem.BootSector;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

/// <summary>
/// Reads and writes the BIOS parameter block plus the 0x55/0xAA boot sector signature.
/// </summary>
public static class FatBootSectorCodec {
    /// <summary>Boot sector size in bytes.</summary>
    public const int BootSectorSize = 512;

    /// <summary>Offset of the 0x55 0xAA boot signature inside the boot sector.</summary>
    public const int BootSignatureOffset = 510;

    /// <summary>Low byte of the boot signature.</summary>
    public const byte BootSignatureLo = 0x55;

    /// <summary>High byte of the boot signature.</summary>
    public const byte BootSignatureHi = 0xAA;

    /// <summary>
    /// Parses a boot sector into a <see cref="MutableBiosParameterBlock"/>.
    /// </summary>
    /// <param name="bootSector">Source boot sector. Must be at least 62 bytes (90 for FAT32).</param>
    /// <param name="fatType">FAT type to interpret the sector as. Determines extended-BPB layout.</param>
    /// <returns>A populated <see cref="MutableBiosParameterBlock"/>.</returns>
    /// <exception cref="InvalidDataException">If the boot signature is missing or the sector is malformed.</exception>
    public static MutableBiosParameterBlock Parse(ReadOnlySpan<byte> bootSector, FatType fatType) {
        if (bootSector.Length < BootSectorSize) {
            throw new InvalidDataException($"Boot sector is {bootSector.Length} bytes; expected {BootSectorSize}.");
        }
        if (bootSector[BootSignatureOffset] != BootSignatureLo || bootSector[BootSignatureOffset + 1] != BootSignatureHi) {
            throw new InvalidDataException($"Boot sector signature 0x{bootSector[BootSignatureOffset]:X2}{bootSector[BootSignatureOffset + 1]:X2} does not match expected 0x55AA.");
        }

        ushort bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.Slice(11, 2));
        if (bytesPerSector == 0) {
            throw new InvalidDataException("BPB BytesPerSector is zero; not a valid FAT volume.");
        }

        MutableBiosParameterBlock bpb = new() {
            BytesPerSector = bytesPerSector,
            SectorsPerCluster = bootSector[13],
            ReservedSectors = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.Slice(14, 2)),
            NumberOfFats = bootSector[16],
            RootDirEntries = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.Slice(17, 2)),
            TotalSectors16 = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.Slice(19, 2)),
            MediaDescriptor = bootSector[21],
            SectorsPerFat = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.Slice(22, 2)),
            SectorsPerTrack = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.Slice(24, 2)),
            NumberOfHeads = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.Slice(26, 2)),
            HiddenSectors = BinaryPrimitives.ReadUInt32LittleEndian(bootSector.Slice(28, 4)),
            TotalSectors32 = BinaryPrimitives.ReadUInt32LittleEndian(bootSector.Slice(32, 4))
        };

        if (fatType == FatType.Fat32) {
            bpb.SectorsPerFat32 = BinaryPrimitives.ReadUInt32LittleEndian(bootSector.Slice(36, 4));
            bpb.RootCluster = BinaryPrimitives.ReadUInt32LittleEndian(bootSector.Slice(44, 4));
            bpb.ExtendedBootSignature = bootSector[66];
            if (bpb.ExtendedBootSignature == 0x29) {
                bpb.VolumeLabel = Encoding.ASCII.GetString(bootSector.Slice(71, 11));
            }
        } else {
            bpb.ExtendedBootSignature = bootSector[38];
            if (bpb.ExtendedBootSignature == 0x29) {
                bpb.VolumeLabel = Encoding.ASCII.GetString(bootSector.Slice(43, 11));
            }
        }

        return bpb;
    }

    /// <summary>
    /// Writes the BPB and 0x55/0xAA boot signature into <paramref name="destination"/>.
    /// Bytes outside the BPB region are not modified, so callers may pre-populate boot code.
    /// </summary>
    /// <param name="bpb">BPB to serialise.</param>
    /// <param name="destination">Destination buffer. Must be at least 512 bytes.</param>
    /// <param name="fatType">FAT type to serialise as.</param>
    /// <exception cref="ArgumentException">If the buffer is too short.</exception>
    /// <exception cref="ArgumentNullException">If <paramref name="bpb"/> is null.</exception>
    public static void Write(MutableBiosParameterBlock bpb, Span<byte> destination, FatType fatType) {
        if (bpb is null) {
            throw new ArgumentNullException(nameof(bpb));
        }
        if (destination.Length < BootSectorSize) {
            throw new ArgumentException($"Boot sector buffer is {destination.Length} bytes; need at least {BootSectorSize}.", nameof(destination));
        }
        bpb.Serialize(destination, fatType);
        destination[BootSignatureOffset] = BootSignatureLo;
        destination[BootSignatureOffset + 1] = BootSignatureHi;
    }
}
