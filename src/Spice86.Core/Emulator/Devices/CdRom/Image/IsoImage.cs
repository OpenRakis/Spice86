using System.Collections.Generic;
using System.Text;

namespace Spice86.Core.Emulator.Devices.CdRom.Image;

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
    private static readonly byte[] ExpectedSignature = "CD001"u8.ToArray();

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

    /// <inheritdoc/>
    public string? UpcEan => null;

    /// <inheritdoc/>
    public string ImagePath { get; }

    /// <inheritdoc/>
    public int Read(int lba, Span<byte> destination, CdSectorMode mode) {
        long byteOffset = (long)lba * SectorSize;
        return _source.Read(byteOffset, destination.Slice(0, SectorSize));
    }

    /// <inheritdoc/>
    public void Dispose() {
        _source.Dispose();
    }
}
