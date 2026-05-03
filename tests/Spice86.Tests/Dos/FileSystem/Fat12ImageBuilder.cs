namespace Spice86.Tests.Dos.FileSystem;

using System;
using System.Text;

/// <summary>
/// Builds a minimal valid FAT12 floppy disk image in memory for unit tests.
/// The default geometry matches a 1.44 MB floppy (2880 sectors, 512 bytes each).
/// </summary>
internal sealed class Fat12ImageBuilder {
    private const int BytesPerSector = 512;
    private const int SectorsPerTrack = 18;
    private const int Heads = 2;
    private const int TotalSectors = 2880;  // 1.44 MB
    private const int ReservedSectors = 1;
    private const int NumberOfFats = 2;
    private const int SectorsPerFat = 9;
    private const int RootDirEntries = 224;
    private const int SectorsPerCluster = 1;
    private const byte MediaDescriptor = 0xF0;

    private readonly byte[] _image;
    private int _nextCluster = 2;
    private readonly ushort[] _fat = new ushort[2849]; // enough for 1.44 MB

    internal Fat12ImageBuilder() {
        _image = new byte[TotalSectors * BytesPerSector];
        WriteBpb();
    }

    private void WriteBpb() {
        Span<byte> boot = _image.AsSpan(0, BytesPerSector);

        // Jump instruction
        boot[0] = 0xEB; boot[1] = 0x3C; boot[2] = 0x90;
        // OEM name
        Encoding.ASCII.GetBytes("SPICE86 ").CopyTo(boot.Slice(3));

        // BPB
        BitConverter.GetBytes((ushort)BytesPerSector).CopyTo(boot.Slice(11));
        boot[13] = SectorsPerCluster;
        BitConverter.GetBytes((ushort)ReservedSectors).CopyTo(boot.Slice(14));
        boot[16] = NumberOfFats;
        BitConverter.GetBytes((ushort)RootDirEntries).CopyTo(boot.Slice(17));
        BitConverter.GetBytes((ushort)TotalSectors).CopyTo(boot.Slice(19));
        boot[21] = MediaDescriptor;
        BitConverter.GetBytes((ushort)SectorsPerFat).CopyTo(boot.Slice(22));
        BitConverter.GetBytes((ushort)SectorsPerTrack).CopyTo(boot.Slice(24));
        BitConverter.GetBytes((ushort)Heads).CopyTo(boot.Slice(26));
        BitConverter.GetBytes(0u).CopyTo(boot.Slice(28)); // hidden sectors
        BitConverter.GetBytes(0u).CopyTo(boot.Slice(32)); // large sectors

        // Extended BPB
        boot[36] = 0x00; // drive number
        boot[37] = 0x00; // reserved
        boot[38] = 0x29; // extended BPB signature
        BitConverter.GetBytes(0x12345678u).CopyTo(boot.Slice(39)); // volume serial
        Encoding.ASCII.GetBytes("TEST FLOPPY").CopyTo(boot.Slice(43)); // volume label (11 bytes)
        Encoding.ASCII.GetBytes("FAT12   ").CopyTo(boot.Slice(54)); // filesystem type (8 bytes)

        // Boot signature
        boot[510] = 0x55;
        boot[511] = 0xAA;
    }

    private int FatStartSector => ReservedSectors;
    private int RootDirStart => FatStartSector + NumberOfFats * SectorsPerFat;
    private int DataStart => RootDirStart + (RootDirEntries * 32 + BytesPerSector - 1) / BytesPerSector;

    /// <summary>
    /// Sets the volume label in the root directory.
    /// </summary>
    internal Fat12ImageBuilder WithVolumeLabel(string label) {
        byte[] rootDir = _image;
        int offset = RootDirStart * BytesPerSector;
        byte[] labelBytes = Encoding.ASCII.GetBytes(label.PadRight(11));
        labelBytes.AsSpan(0, Math.Min(11, labelBytes.Length)).CopyTo(rootDir.AsSpan(offset));
        rootDir[offset + 11] = 0x08; // volume label attribute
        return this;
    }

    /// <summary>
    /// Adds a file in the root directory with the given contents.
    /// </summary>
    /// <param name="dosName">8.3 DOS name (e.g. "README  TXT" — exactly 11 chars) or "FILENAME.EXT" format.</param>
    /// <param name="content">File content bytes.</param>
    internal Fat12ImageBuilder WithFile(string dosName, byte[] content) {
        AddDirectoryEntry(RootDirStart * BytesPerSector, dosName, content, isDirectory: false);
        return this;
    }

    /// <summary>
    /// Adds a subdirectory in the root and optionally a file inside it.
    /// </summary>
    internal Fat12ImageBuilder WithSubdirectoryAndFile(string dirName, string fileName, byte[] content) {
        int dirCluster = AllocateClusters(1);
        AddDirectoryEntry(RootDirStart * BytesPerSector, dirName, null, isDirectory: true, firstCluster: (ushort)dirCluster);

        int dirSector = DataStart + (dirCluster - 2) * SectorsPerCluster;
        AddDirectoryEntry(dirSector * BytesPerSector, fileName, content, isDirectory: false);
        return this;
    }

    internal byte[] Build() {
        WriteAllFats();
        return _image;
    }

    private void AddDirectoryEntry(int dirByteOffset, string name, byte[]? content, bool isDirectory, ushort firstCluster = 0) {
        // Find free slot
        int entryOffset = dirByteOffset;
        while (entryOffset < dirByteOffset + RootDirEntries * 32 && _image[entryOffset] != 0) {
            entryOffset += 32;
        }
        if (entryOffset >= dirByteOffset + RootDirEntries * 32) {
            throw new InvalidOperationException("No free directory entry slots.");
        }

        string[] parts = name.Contains('.') ? name.Split('.') : new[] { name, string.Empty };
        string baseName = parts[0].PadRight(8);
        string ext = parts.Length > 1 ? parts[1].PadRight(3) : "   ";

        if (content != null && content.Length > 0) {
            int clustersNeeded = (content.Length + BytesPerSector * SectorsPerCluster - 1) / (BytesPerSector * SectorsPerCluster);
            firstCluster = (ushort)AllocateClusters(clustersNeeded);
            WriteFileData(firstCluster, content);
        }

        Span<byte> entry = _image.AsSpan(entryOffset, 32);
        Encoding.ASCII.GetBytes(baseName).AsSpan(0, 8).CopyTo(entry);
        Encoding.ASCII.GetBytes(ext).AsSpan(0, 3).CopyTo(entry.Slice(8));
        entry[11] = isDirectory ? (byte)0x10 : (byte)0x20;
        BitConverter.GetBytes(firstCluster).CopyTo(entry.Slice(26));
        BitConverter.GetBytes(content?.Length ?? 0).CopyTo(entry.Slice(28));
    }

    private int AllocateClusters(int count) {
        int first = _nextCluster;
        for (int i = 0; i < count; i++) {
            int cluster = _nextCluster++;
            if (i == count - 1) {
                _fat[cluster] = 0xFFF; // end of chain
            } else {
                _fat[cluster] = (ushort)(_nextCluster); // next cluster
            }
        }
        return first;
    }

    private void WriteFileData(ushort firstCluster, byte[] content) {
        ushort current = firstCluster;
        int written = 0;
        while (written < content.Length) {
            int sector = DataStart + (current - 2) * SectorsPerCluster;
            int byteOffset = sector * BytesPerSector;
            int toCopy = Math.Min(BytesPerSector * SectorsPerCluster, content.Length - written);
            content.AsSpan(written, toCopy).CopyTo(_image.AsSpan(byteOffset, toCopy));
            written += toCopy;
            ushort next = _fat[current];
            if (next >= 0xFF8) {
                break;
            }
            current = next;
        }
    }

    private void WriteAllFats() {
        for (int fatIndex = 0; fatIndex < NumberOfFats; fatIndex++) {
            int fatByteOffset = (FatStartSector + fatIndex * SectorsPerFat) * BytesPerSector;
            // FAT12: each entry is 12 bits (1.5 bytes)
            for (int n = 0; n < _fat.Length; n++) {
                int byteIndex = fatByteOffset + n * 3 / 2;
                if (byteIndex + 1 >= fatByteOffset + SectorsPerFat * BytesPerSector) {
                    break;
                }
                ushort value = _fat[n];
                if ((n & 1) == 0) {
                    _image[byteIndex] = (byte)(value & 0xFF);
                    _image[byteIndex + 1] = (byte)((_image[byteIndex + 1] & 0xF0) | ((value >> 8) & 0x0F));
                } else {
                    _image[byteIndex] = (byte)((_image[byteIndex] & 0x0F) | ((value & 0x0F) << 4));
                    _image[byteIndex + 1] = (byte)(value >> 4);
                }
            }
            // FAT12 media descriptor
            _image[fatByteOffset] = MediaDescriptor;
            _image[fatByteOffset + 1] = 0xFF;
            _image[fatByteOffset + 2] = 0xFF;
        }
    }
}
