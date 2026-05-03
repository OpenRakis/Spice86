namespace Spice86.Core.Emulator.Devices.CdRom.Image;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Builds an ISO 9660 CD-ROM image in memory from the files in a host directory
/// and exposes it as an <see cref="ICdRomImage"/>.
/// Only the root directory is populated; subdirectories are not traversed.
/// </summary>
public sealed class VirtualIsoImage : ICdRomImage {
    private const int SectorSize = 2048;
    private const int SystemAreaLba = 0;
    private const int PvdLba = 16;
    private const int VdstLba = 17;
    private const int PathTableLeLba = 18;
    private const int PathTableBeLba = 19;
    private const int RootDirLba = 20;
    private const int FirstFileLba = 21;

    private readonly byte[] _imageData;
    private readonly MemoryDataSource _source;
    private readonly List<CdTrack> _tracks;
    private readonly string _sourceDirectory;

    /// <summary>
    /// Builds a virtual ISO image from all files in <paramref name="sourceDirectory"/>.
    /// </summary>
    /// <param name="sourceDirectory">Host directory whose root-level files are added to the image.</param>
    /// <param name="volumeLabel">Volume label written into the PVD (up to 32 characters).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceDirectory"/> does not exist.</exception>
    public VirtualIsoImage(string sourceDirectory, string volumeLabel) {
        _sourceDirectory = sourceDirectory;
        _imageData = BuildImage(sourceDirectory, volumeLabel);
        _source = new MemoryDataSource(_imageData);
        TotalSectors = _imageData.Length / SectorSize;
        PrimaryVolume = ParsePrimaryVolume(_imageData);
        _tracks = new List<CdTrack> {
            new CdTrack(
                number: 1,
                startLba: 0,
                lengthSectors: TotalSectors,
                sectorSize: SectorSize,
                mode: CdSectorMode.CookedData2048,
                isAudio: false,
                pregap: 0,
                postgap: 0,
                source: _source,
                fileOffset: 0)
        };
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
    public string ImagePath => _sourceDirectory;

    /// <inheritdoc/>
    public int Read(int lba, Span<byte> destination, CdSectorMode mode) {
        long byteOffset = (long)lba * SectorSize;
        return _source.Read(byteOffset, destination.Slice(0, Math.Min(SectorSize, destination.Length)));
    }

    /// <inheritdoc/>
    public void Dispose() {
        // Nothing to dispose — all data is in managed memory.
    }

    private static byte[] BuildImage(string sourceDirectory, string volumeLabel) {
        string[] files = Directory.GetFiles(sourceDirectory);

        int totalLbas = FirstFileLba;
        List<(string Path, int Lba, int Length)> fileEntries = new();
        foreach (string file in files) {
            int fileLength = (int)new FileInfo(file).Length;
            fileEntries.Add((file, totalLbas, fileLength));
            int fileSectors = (fileLength + SectorSize - 1) / SectorSize;
            totalLbas += fileSectors == 0 ? 1 : fileSectors;
        }

        byte[] image = new byte[totalLbas * SectorSize];

        // Root directory at LBA 20
        int rootDirSize = BuildRootDirectory(image, fileEntries);

        // Path table (LE) at LBA 18
        int pathTableSize = BuildPathTableLe(image, PathTableLeLba * SectorSize);

        // Path table (BE) at LBA 19
        BuildPathTableBe(image, PathTableBeLba * SectorSize);

        // PVD at LBA 16
        BuildPvd(image, volumeLabel, totalLbas, rootDirSize, pathTableSize);

        // Volume Descriptor Set Terminator at LBA 17
        BuildVdst(image);

        // File data
        foreach ((string filePath, int lba, int fileLength) in fileEntries) {
            byte[] fileData = File.ReadAllBytes(filePath);
            fileData.AsSpan().CopyTo(image.AsSpan(lba * SectorSize, fileData.Length));
        }

        return image;
    }

    private static int BuildRootDirectory(byte[] image, List<(string Path, int Lba, int Length)> fileEntries) {
        int offset = RootDirLba * SectorSize;
        int written = 0;

        // "." entry (current directory)
        written += WriteDirectoryRecord(image, offset + written, "\0", RootDirLba, SectorSize, isDirectory: true);
        // ".." entry (parent directory)
        written += WriteDirectoryRecord(image, offset + written, "\x01", RootDirLba, SectorSize, isDirectory: true);

        foreach ((string filePath, int lba, int fileLength) in fileEntries) {
            string name = Path.GetFileName(filePath).ToUpperInvariant() + ";1";
            written += WriteDirectoryRecord(image, offset + written, name, lba, fileLength, isDirectory: false);
        }

        return written;
    }

    private static int WriteDirectoryRecord(byte[] image, int offset, string identifier, int lba, int dataLength, bool isDirectory) {
        byte[] nameBytes = Encoding.ASCII.GetBytes(identifier);
        int recordLength = 33 + nameBytes.Length;
        if ((recordLength & 1) != 0) {
            recordLength++;
        }
        if (offset + recordLength > image.Length) {
            return 0;
        }
        Span<byte> record = image.AsSpan(offset, recordLength);
        record.Clear();
        record[0] = (byte)recordLength;
        WriteBothEndian32(record.Slice(2), lba);
        WriteBothEndian32(record.Slice(10), dataLength);
        WriteRecordingDate(record.Slice(18));
        record[25] = isDirectory ? (byte)0x02 : (byte)0x00;
        WriteBothEndian16(record.Slice(28), 1);
        record[32] = (byte)nameBytes.Length;
        nameBytes.AsSpan().CopyTo(record.Slice(33));
        return recordLength;
    }

    private static int BuildPathTableLe(byte[] image, int offset) {
        // Minimal path table: one entry for the root directory
        // Entry: length(1) + extended(1) + LBA LE(4) + parent dir number LE(2) + dir ID(1) + padding(1)
        image[offset] = 1;
        image[offset + 1] = 0;
        BitConverter.GetBytes(RootDirLba).CopyTo(image, offset + 2);
        BitConverter.GetBytes((ushort)1).CopyTo(image, offset + 6);
        image[offset + 8] = 0x00;
        image[offset + 9] = 0x00;
        return 10;
    }

    private static void BuildPathTableBe(byte[] image, int offset) {
        image[offset] = 1;
        image[offset + 1] = 0;
        WriteUint32Be(image, offset + 2, RootDirLba);
        image[offset + 6] = 0x00;
        image[offset + 7] = 0x01;
        image[offset + 8] = 0x00;
        image[offset + 9] = 0x00;
    }

    private static void BuildPvd(byte[] image, string volumeLabel, int volumeSpaceSize, int rootDirSize, int pathTableSize) {
        int pvdOffset = PvdLba * SectorSize;
        Span<byte> pvd = image.AsSpan(pvdOffset, SectorSize);
        pvd.Clear();
        pvd[0] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(pvd.Slice(1));
        pvd[6] = 0x01;
        // System identifier (32 spaces at offset 8)
        FillSpaces(pvd.Slice(8, 32));
        // Volume identifier (32 chars at offset 40)
        string paddedLabel = volumeLabel.Length > 32 ? volumeLabel[..32] : volumeLabel.PadRight(32);
        Encoding.ASCII.GetBytes(paddedLabel).CopyTo(pvd.Slice(40));
        // Volume space size (offset 80, both-endian 32-bit)
        WriteBothEndian32(pvd.Slice(80), volumeSpaceSize);
        // Volume set size = 1 (offset 120)
        WriteBothEndian16(pvd.Slice(120), 1);
        // Volume sequence number = 1 (offset 124)
        WriteBothEndian16(pvd.Slice(124), 1);
        // Logical block size = 2048 (offset 128)
        WriteBothEndian16(pvd.Slice(128), SectorSize);
        // Path table size (offset 132)
        WriteBothEndian32(pvd.Slice(132), pathTableSize);
        // Location of L path table (offset 140, LE 32-bit)
        BitConverter.GetBytes(PathTableLeLba).CopyTo(pvd.Slice(140));
        // Location of M path table (offset 148, BE 32-bit)
        WriteUint32BeSp(pvd.Slice(148), PathTableBeLba);
        // Root directory record at offset 156 (34 bytes)
        BuildRootDirRecord(pvd.Slice(156), rootDirSize);
        // Volume set identifier (128 spaces at offset 190)
        FillSpaces(pvd.Slice(190, 128));
        // Publisher identifier (128 spaces at offset 318)
        FillSpaces(pvd.Slice(318, 128));
        // Data preparer (128 spaces at offset 446)
        FillSpaces(pvd.Slice(446, 128));
        // Application identifier (128 spaces at offset 574)
        FillSpaces(pvd.Slice(574, 128));
    }

    private static void BuildRootDirRecord(Span<byte> record, int rootDirSize) {
        record[0] = 34;
        record[1] = 0;
        WriteBothEndian32(record.Slice(2), RootDirLba);
        WriteBothEndian32(record.Slice(10), rootDirSize);
        WriteRecordingDate(record.Slice(18));
        record[25] = 0x02;
        WriteBothEndian16(record.Slice(28), 1);
        record[32] = 1;
        record[33] = 0x00;
    }

    private static void BuildVdst(byte[] image) {
        int offset = VdstLba * SectorSize;
        image[offset] = 0xFF;
        Encoding.ASCII.GetBytes("CD001").CopyTo(image, offset + 1);
        image[offset + 6] = 0x01;
    }

    private static void WriteBothEndian32(Span<byte> target, int value) {
        target[0] = (byte)(value & 0xFF);
        target[1] = (byte)((value >> 8) & 0xFF);
        target[2] = (byte)((value >> 16) & 0xFF);
        target[3] = (byte)((value >> 24) & 0xFF);
        target[4] = (byte)((value >> 24) & 0xFF);
        target[5] = (byte)((value >> 16) & 0xFF);
        target[6] = (byte)((value >> 8) & 0xFF);
        target[7] = (byte)(value & 0xFF);
    }

    private static void WriteBothEndian16(Span<byte> target, int value) {
        target[0] = (byte)(value & 0xFF);
        target[1] = (byte)((value >> 8) & 0xFF);
        target[2] = (byte)((value >> 8) & 0xFF);
        target[3] = (byte)(value & 0xFF);
    }

    private static void WriteUint32Be(byte[] image, int offset, int value) {
        image[offset] = (byte)((value >> 24) & 0xFF);
        image[offset + 1] = (byte)((value >> 16) & 0xFF);
        image[offset + 2] = (byte)((value >> 8) & 0xFF);
        image[offset + 3] = (byte)(value & 0xFF);
    }

    private static void WriteUint32BeSp(Span<byte> target, int value) {
        target[0] = (byte)((value >> 24) & 0xFF);
        target[1] = (byte)((value >> 16) & 0xFF);
        target[2] = (byte)((value >> 8) & 0xFF);
        target[3] = (byte)(value & 0xFF);
    }

    private static void WriteRecordingDate(Span<byte> target) {
        // Year since 1900; use 2000 (100), month 1, day 1
        target[0] = 100;
        target[1] = 1;
        target[2] = 1;
        target[3] = 0;
        target[4] = 0;
        target[5] = 0;
        target[6] = 0;
    }

    private static void FillSpaces(Span<byte> target) {
        for (int i = 0; i < target.Length; i++) {
            target[i] = 0x20;
        }
    }

    private static IsoVolumeDescriptor ParsePrimaryVolume(byte[] image) {
        int pvdOffset = PvdLba * SectorSize;
        int volumeSpaceSize = BitConverter.ToInt32(image, pvdOffset + 80);
        int logicalBlockSize = BitConverter.ToUInt16(image, pvdOffset + 128);
        int rootDirLba = BitConverter.ToInt32(image, pvdOffset + 156 + 2);
        int rootDirSize = BitConverter.ToInt32(image, pvdOffset + 156 + 10);
        string volumeId = Encoding.ASCII.GetString(image, pvdOffset + 40, 32).TrimEnd();
        return new IsoVolumeDescriptor(volumeId, rootDirLba, rootDirSize, logicalBlockSize, volumeSpaceSize);
    }
}
