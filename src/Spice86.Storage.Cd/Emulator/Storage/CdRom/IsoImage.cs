using System.Collections.Generic;
using System.Text;

using System.Buffers.Binary;

namespace Spice86.Shared.Emulator.Storage.CdRom;

/// <summary>Reads and exposes a plain ISO 9660 image file as an <see cref="ICdRomImage"/>.</summary>
public sealed class IsoImage : ICdRomImage {
    private const int PvdLba = 16;
    private const int SectorSize = 2048;
    private const int PvdSignatureOffset = 1;
    private const int PvdVolumeSpaceSizeOffset = 80;
    private const int PvdLogicalBlockSizeOffset = 128;
    private const int PvdRootDirRecordOffset = 156;
    private const int PvdVolumeIdentifierOffset = 40;
    private const int PvdVolumeIdentifierLength = 32;
    private const int SvdEscapeSequenceOffset = 88;
    private const int MaxVolumeDescriptorsToScan = 32;
    private static readonly byte[] ExpectedSignature = "CD001"u8.ToArray();

    private enum VolumeDescriptorType : byte {
        Primary = 0x01,
        Supplementary = 0x02,
        Terminator = 0xFF
    }

    private readonly FileBackedDataSource _source;
    private readonly List<CdTrack> _tracks;

    /// <summary>Opens an ISO image file and parses its Primary Volume Descriptor.</summary>
    /// <param name="isoFilePath">Path to the .iso file.</param>
    /// <exception cref="InvalidDataException">Thrown when the file is not a valid ISO 9660 image.</exception>
    public IsoImage(string isoFilePath) {
        ImagePath = isoFilePath;
        _source = new FileBackedDataSource(isoFilePath);

        byte[] pvdBuffer = new byte[SectorSize];
        _source.Read((long)PvdLba * SectorSize, pvdBuffer);
        ValidateSignature(pvdBuffer);

        int volumeSpaceSize = BinaryPrimitives.ReadInt32LittleEndian(pvdBuffer.AsSpan(PvdVolumeSpaceSizeOffset, 4));
        int logicalBlockSize = BinaryPrimitives.ReadUInt16LittleEndian(pvdBuffer.AsSpan(PvdLogicalBlockSizeOffset, 2));

        ReadOnlySpan<byte> rootDirSpan = pvdBuffer.AsSpan(PvdRootDirRecordOffset, 34);
        int rootDirLba = BinaryPrimitives.ReadInt32LittleEndian(rootDirSpan.Slice(2, 4));
        int rootDirSize = BinaryPrimitives.ReadInt32LittleEndian(rootDirSpan.Slice(10, 4));

        string volumeId = Encoding.ASCII.GetString(pvdBuffer, PvdVolumeIdentifierOffset, PvdVolumeIdentifierLength).TrimEnd();

        PrimaryVolume = new IsoVolumeDescriptor(volumeId, rootDirLba, rootDirSize, logicalBlockSize, volumeSpaceSize);
        TotalSectors = volumeSpaceSize;

        ScanSupplementaryVolumeDescriptors();

        CdTrack track = new CdTrack(
            number: 1,
            startLba: 0,
            lengthSectors: volumeSpaceSize,
            sectorSize: SectorSize,
            mode: CdSectorMode.CookedData2048,
            isAudio: false,
            pregap: 0,
            postgap: 0,
            source: _source,
            fileOffset: 0);
        _tracks = new List<CdTrack> { track };
    }

    private static void ValidateSignature(byte[] pvdBuffer) {
        for (int i = 0; i < ExpectedSignature.Length; i++) {
            if (pvdBuffer[PvdSignatureOffset + i] != ExpectedSignature[i]) {
                throw new InvalidDataException("File is not a valid ISO 9660 image: missing CD001 signature.");
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<CdTrack> Tracks => _tracks;

    /// <inheritdoc/>
    public int TotalSectors { get; }

    /// <inheritdoc/>
    public IsoVolumeDescriptor PrimaryVolume { get; }

    /// <summary>
    /// Joliet Supplementary Volume Descriptor when this image contains one, otherwise <c>null</c>.
    /// </summary>
    public IsoSupplementaryVolumeDescriptor? JolietVolume { get; private set; }

    /// <summary>
    /// Reads the Joliet root directory and returns its entries with UCS-2 BE
    /// decoded names. Throws when no Joliet volume is present.
    /// </summary>
    public IReadOnlyList<IsoDirectoryRecord> ReadJolietRootDirectory() {
        if (JolietVolume is null) {
            throw new InvalidOperationException("This ISO image does not contain a Joliet supplementary volume descriptor.");
        }
        int totalBytes = JolietVolume.RootDirectorySize;
        int firstLba = JolietVolume.RootDirectoryLba;
        byte[] buffer = new byte[totalBytes];
        int sectorsToRead = (totalBytes + SectorSize - 1) / SectorSize;
        for (int i = 0; i < sectorsToRead; i++) {
            int bytesRemaining = totalBytes - i * SectorSize;
            int chunk = bytesRemaining >= SectorSize ? SectorSize : bytesRemaining;
            byte[] sector = new byte[SectorSize];
            _source.Read((long)(firstLba + i) * SectorSize, sector);
            Buffer.BlockCopy(sector, 0, buffer, i * SectorSize, chunk);
        }

        List<IsoDirectoryRecord> records = new();
        int offset = 0;
        while (offset < totalBytes) {
            byte recordLength = buffer[offset];
            if (recordLength == 0) {
                // Records do not span sector boundaries; advance to next sector.
                int nextSectorStart = ((offset / SectorSize) + 1) * SectorSize;
                if (nextSectorStart >= totalBytes) {
                    break;
                }
                offset = nextSectorStart;
                continue;
            }
            IsoDirectoryRecord record = IsoDirectoryRecord.ParseJoliet(buffer.AsSpan(offset, recordLength));
            records.Add(record);
            offset += recordLength;
        }
        return records;
    }

    private void ScanSupplementaryVolumeDescriptors() {
        byte[] sector = new byte[SectorSize];
        for (int i = 1; i < MaxVolumeDescriptorsToScan; i++) {
            int lba = PvdLba + i;
            long offset = (long)lba * SectorSize;
            if (offset + SectorSize > _source.LengthBytes) {
                return;
            }
            _source.Read(offset, sector);
            byte type = sector[0];
            if (type == (byte)VolumeDescriptorType.Terminator) {
                return;
            }
            if (!HasCd001Signature(sector)) {
                return;
            }
            if (type != (byte)VolumeDescriptorType.Supplementary) {
                continue;
            }
            IsoSupplementaryVolumeDescriptor svd = ParseSupplementaryVolumeDescriptor(sector);
            if (svd.IsJoliet && JolietVolume is null) {
                JolietVolume = svd;
            }
        }
    }

    private static bool HasCd001Signature(byte[] sector) {
        for (int i = 0; i < ExpectedSignature.Length; i++) {
            if (sector[PvdSignatureOffset + i] != ExpectedSignature[i]) {
                return false;
            }
        }
        return true;
    }

    private static IsoSupplementaryVolumeDescriptor ParseSupplementaryVolumeDescriptor(byte[] sector) {
        byte[] escape = new byte[3];
        escape[0] = sector[SvdEscapeSequenceOffset];
        escape[1] = sector[SvdEscapeSequenceOffset + 1];
        escape[2] = sector[SvdEscapeSequenceOffset + 2];
        int volumeSpaceSize = BinaryPrimitives.ReadInt32LittleEndian(sector.AsSpan(PvdVolumeSpaceSizeOffset, 4));
        int logicalBlockSize = BinaryPrimitives.ReadUInt16LittleEndian(sector.AsSpan(PvdLogicalBlockSizeOffset, 2));
        ReadOnlySpan<byte> rootDirSpan = sector.AsSpan(PvdRootDirRecordOffset, 34);
        int rootDirLba = BinaryPrimitives.ReadInt32LittleEndian(rootDirSpan.Slice(2, 4));
        int rootDirSize = BinaryPrimitives.ReadInt32LittleEndian(rootDirSpan.Slice(10, 4));
        string volumeId = DecodeVolumeIdentifier(sector, escape);
        return new IsoSupplementaryVolumeDescriptor(volumeId, rootDirLba, rootDirSize, logicalBlockSize, volumeSpaceSize, escape);
    }

    private static string DecodeVolumeIdentifier(byte[] sector, byte[] escapeSequence) {
        ReadOnlySpan<byte> raw = sector.AsSpan(PvdVolumeIdentifierOffset, PvdVolumeIdentifierLength);
        bool joliet = escapeSequence[0] == 0x25 && escapeSequence[1] == 0x2F
            && (escapeSequence[2] == 0x40 || escapeSequence[2] == 0x43 || escapeSequence[2] == 0x45);
        if (joliet) {
            return Encoding.BigEndianUnicode.GetString(raw).TrimEnd('\0', ' ');
        }
        return Encoding.ASCII.GetString(raw).TrimEnd();
    }

    /// <inheritdoc/>
    public string? UpcEan => null;

    /// <inheritdoc/>
    public string ImagePath { get; }

    /// <inheritdoc/>
    public int Read(int lba, Span<byte> destination, CdSectorMode mode) {
        // Plain ISO images expose cooked 2048-byte sectors only.
        if (mode != CdSectorMode.CookedData2048) {
            return 0;
        }
        if (destination.Length < SectorSize) {
            return 0;
        }

        long byteOffset = (long)lba * SectorSize;
        int bytesRead = _source.Read(byteOffset, destination.Slice(0, SectorSize));
        if (bytesRead != SectorSize) {
            return 0;
        }

        return bytesRead;
    }

    /// <inheritdoc/>
    public void Dispose() {
        _source.Dispose();
    }
}
