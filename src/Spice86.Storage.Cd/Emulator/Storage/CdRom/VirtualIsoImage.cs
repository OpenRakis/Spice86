namespace Spice86.Shared.Emulator.Storage.CdRom;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Builds an ISO 9660 CD-ROM image in memory from a host directory tree
/// (files and subdirectories, recursively) and exposes it as an <see cref="ICdRomImage"/>.
/// </summary>
public sealed class VirtualIsoImage : ICdRomImage {
    private const int SectorSize = 2048;
    private const int SystemAreaLba = 0;
    private const int PvdLba = 16;
    private const int VdstLba = 17;
    private const int PathTableLeLba = 18;
    private const int PathTableBeLba = 19;
    private const int RootDirLba = 20;

    private readonly byte[] _imageData;
    private readonly MemoryDataSource _source;
    private readonly List<CdTrack> _tracks;
    private readonly string _sourceDirectory;

    /// <summary>
    /// Builds a virtual ISO image from <paramref name="sourceDirectory"/>, traversing
    /// subdirectories recursively.
    /// </summary>
    /// <param name="sourceDirectory">Host directory whose contents (files and subdirectories) are added to the image.</param>
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
        DirNode root = ScanDirectory(sourceDirectory, parent: null, name: string.Empty);
        AssignDirectorySizes(root);

        List<DirNode> allDirs = new();
        CollectDirsBreadthFirst(root, allDirs);
        for (int i = 0; i < allDirs.Count; i++) {
            allDirs[i].DirNumber = i + 1;
        }

        int currentLba = RootDirLba;
        foreach (DirNode dir in allDirs) {
            dir.Lba = currentLba;
            currentLba += dir.SizeBytes / SectorSize;
        }
        foreach (DirNode dir in allDirs) {
            foreach (FileEntry file in dir.Files) {
                file.Lba = currentLba;
                int fileSectors = (file.Length + SectorSize - 1) / SectorSize;
                currentLba += fileSectors == 0 ? 1 : fileSectors;
            }
        }
        int totalLbas = currentLba;

        int pathTableSize = ComputePathTableSize(allDirs);

        byte[] image = new byte[totalLbas * SectorSize];

        foreach (DirNode dir in allDirs) {
            WriteDirectoryContents(image, dir);
        }

        WritePathTableLe(image, PathTableLeLba * SectorSize, allDirs);
        WritePathTableBe(image, PathTableBeLba * SectorSize, allDirs);

        BuildPvd(image, volumeLabel, totalLbas, root.SizeBytes, pathTableSize);
        BuildVdst(image);

        foreach (DirNode dir in allDirs) {
            foreach (FileEntry file in dir.Files) {
                byte[] data = File.ReadAllBytes(file.SourcePath);
                data.AsSpan().CopyTo(image.AsSpan(file.Lba * SectorSize, data.Length));
            }
        }

        return image;
    }

    private static DirNode ScanDirectory(string path, DirNode? parent, string name) {
        DirNode node = new DirNode {
            Name = name,
            Parent = parent
        };
        foreach (string filePath in Directory.GetFiles(path)) {
            int length = (int)new FileInfo(filePath).Length;
            string isoName = Path.GetFileName(filePath).ToUpperInvariant() + ";1";
            node.Files.Add(new FileEntry {
                SourcePath = filePath,
                IsoName = isoName,
                Length = length
            });
        }
        foreach (string subDirPath in Directory.GetDirectories(path)) {
            string subName = Path.GetFileName(subDirPath).ToUpperInvariant();
            node.Subdirs.Add(ScanDirectory(subDirPath, node, subName));
        }
        return node;
    }

    private static void AssignDirectorySizes(DirNode root) {
        Stack<DirNode> stack = new();
        stack.Push(root);
        while (stack.Count > 0) {
            DirNode dir = stack.Pop();
            dir.SizeBytes = ComputeDirectorySize(dir);
            foreach (DirNode child in dir.Subdirs) {
                stack.Push(child);
            }
        }
    }

    private static int ComputeDirectorySize(DirNode dir) {
        int posInSector = 0;
        int sectors = 1;
        foreach (int recLen in EnumerateRecordLengths(dir)) {
            if (posInSector + recLen > SectorSize) {
                sectors++;
                posInSector = recLen;
            } else {
                posInSector += recLen;
            }
        }
        return sectors * SectorSize;
    }

    private static IEnumerable<int> EnumerateRecordLengths(DirNode dir) {
        yield return RoundUpEven(33 + 1); // "."
        yield return RoundUpEven(33 + 1); // ".."
        foreach (DirNode sub in dir.Subdirs) {
            yield return RoundUpEven(33 + Encoding.ASCII.GetByteCount(sub.Name));
        }
        foreach (FileEntry file in dir.Files) {
            yield return RoundUpEven(33 + Encoding.ASCII.GetByteCount(file.IsoName));
        }
    }

    private static int RoundUpEven(int value) {
        return (value & 1) == 0 ? value : value + 1;
    }

    private static void CollectDirsBreadthFirst(DirNode root, List<DirNode> output) {
        Queue<DirNode> queue = new();
        queue.Enqueue(root);
        while (queue.Count > 0) {
            DirNode dir = queue.Dequeue();
            output.Add(dir);
            foreach (DirNode sub in dir.Subdirs) {
                queue.Enqueue(sub);
            }
        }
    }

    private static void WriteDirectoryContents(byte[] image, DirNode dir) {
        int baseOffset = dir.Lba * SectorSize;
        int sector = 0;
        int posInSector = 0;
        int parentLba = dir.Parent != null ? dir.Parent.Lba : dir.Lba;
        int parentSize = dir.Parent != null ? dir.Parent.SizeBytes : dir.SizeBytes;

        WriteRecord(image, ref sector, ref posInSector, baseOffset, "\0", dir.Lba, dir.SizeBytes, isDirectory: true);
        WriteRecord(image, ref sector, ref posInSector, baseOffset, "\x01", parentLba, parentSize, isDirectory: true);

        foreach (DirNode sub in dir.Subdirs) {
            WriteRecord(image, ref sector, ref posInSector, baseOffset, sub.Name, sub.Lba, sub.SizeBytes, isDirectory: true);
        }
        foreach (FileEntry file in dir.Files) {
            WriteRecord(image, ref sector, ref posInSector, baseOffset, file.IsoName, file.Lba, file.Length, isDirectory: false);
        }
    }

    private static void WriteRecord(byte[] image, ref int sector, ref int posInSector, int baseOffset,
        string identifier, int lba, int dataLength, bool isDirectory) {
        byte[] nameBytes = Encoding.ASCII.GetBytes(identifier);
        int recordLength = RoundUpEven(33 + nameBytes.Length);
        if (posInSector + recordLength > SectorSize) {
            sector++;
            posInSector = 0;
        }
        int offset = baseOffset + sector * SectorSize + posInSector;
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
        posInSector += recordLength;
    }

    private static int ComputePathTableSize(List<DirNode> dirs) {
        int total = 0;
        foreach (DirNode dir in dirs) {
            total += PathTableEntrySize(dir);
        }
        return total;
    }

    private static int PathTableEntrySize(DirNode dir) {
        int nameLen = dir.Parent == null ? 1 : Encoding.ASCII.GetByteCount(dir.Name);
        return RoundUpEven(8 + nameLen);
    }

    private static void WritePathTableLe(byte[] image, int offset, List<DirNode> dirs) {
        int pos = offset;
        foreach (DirNode dir in dirs) {
            byte[] nameBytes = dir.Parent == null ? new byte[] { 0x00 } : Encoding.ASCII.GetBytes(dir.Name);
            int parentDirNumber = dir.Parent == null ? 1 : dir.Parent.DirNumber;
            image[pos] = (byte)nameBytes.Length;
            image[pos + 1] = 0;
            BitConverter.GetBytes(dir.Lba).CopyTo(image, pos + 2);
            BitConverter.GetBytes((ushort)parentDirNumber).CopyTo(image, pos + 6);
            nameBytes.AsSpan().CopyTo(image.AsSpan(pos + 8, nameBytes.Length));
            int entrySize = RoundUpEven(8 + nameBytes.Length);
            if (entrySize > 8 + nameBytes.Length) {
                image[pos + 8 + nameBytes.Length] = 0;
            }
            pos += entrySize;
        }
    }

    private static void WritePathTableBe(byte[] image, int offset, List<DirNode> dirs) {
        int pos = offset;
        foreach (DirNode dir in dirs) {
            byte[] nameBytes = dir.Parent == null ? new byte[] { 0x00 } : Encoding.ASCII.GetBytes(dir.Name);
            int parentDirNumber = dir.Parent == null ? 1 : dir.Parent.DirNumber;
            image[pos] = (byte)nameBytes.Length;
            image[pos + 1] = 0;
            WriteUint32Be(image, pos + 2, dir.Lba);
            image[pos + 6] = (byte)((parentDirNumber >> 8) & 0xFF);
            image[pos + 7] = (byte)(parentDirNumber & 0xFF);
            nameBytes.AsSpan().CopyTo(image.AsSpan(pos + 8, nameBytes.Length));
            int entrySize = RoundUpEven(8 + nameBytes.Length);
            if (entrySize > 8 + nameBytes.Length) {
                image[pos + 8 + nameBytes.Length] = 0;
            }
            pos += entrySize;
        }
    }

    private sealed class DirNode {
        public string Name = string.Empty;
        public DirNode? Parent;
        public List<DirNode> Subdirs = new();
        public List<FileEntry> Files = new();
        public int Lba;
        public int SizeBytes;
        public int DirNumber;
    }

    private sealed class FileEntry {
        public string SourcePath = string.Empty;
        public string IsoName = string.Empty;
        public int Lba;
        public int Length;
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
        // Years since 1900; 100 represents the year 2000.
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
