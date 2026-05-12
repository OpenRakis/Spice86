namespace Spice86.Core.Emulator.OperatingSystem.FileSystem;

using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Builds a FAT12 1.44 MB floppy disk image from a host directory.
/// Files in the root of the source directory are added to the image root.
/// One level of subdirectories is also supported.
/// Files that do not fit in the remaining clusters are skipped with a warning.
/// </summary>
public sealed class VirtualFloppyImage {
    private const int TotalSectors = 2880;
    private const int BytesPerSector = 512;
    private const int SectorsPerCluster = 1;
    private const int ReservedSectors = 1;
    private const int NumberOfFats = 2;
    private const int SectorsPerFat = 9;
    private const int RootDirEntries = 224;
    private const int SectorsPerTrack = 18;
    private const int NumberOfHeads = 2;
    private const byte MediaDescriptor = 0xF0;

    private static readonly int RootDirSectors = (RootDirEntries * 32 + BytesPerSector - 1) / BytesPerSector;
    private static readonly int FatStartSector = ReservedSectors;
    private static readonly int RootDirStartSector = FatStartSector + NumberOfFats * SectorsPerFat;
    private static readonly int DataStartSector = RootDirStartSector + RootDirSectors;
    private static readonly int TotalDataClusters = TotalSectors - DataStartSector;
    private static readonly int ImageSize = TotalSectors * BytesPerSector;

    private readonly string _sourceDirectory;
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initialises a new <see cref="VirtualFloppyImage"/> that will read from <paramref name="sourceDirectory"/>.
    /// </summary>
    /// <param name="sourceDirectory">Path to the host directory whose contents will be written to the image.</param>
    /// <param name="loggerService">Logger used to emit warnings for files that do not fit.</param>
    public VirtualFloppyImage(string sourceDirectory, ILoggerService loggerService) {
        _sourceDirectory = sourceDirectory;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Builds the floppy disk image and returns it as a byte array of <c>1,474,560</c> bytes.
    /// </summary>
    /// <returns>Raw bytes of the 1.44 MB FAT12 floppy image.</returns>
    public byte[] Build() {
        byte[] image = new byte[ImageSize];
        ushort[] fat = new ushort[TotalDataClusters + 2];
        fat[0] = (ushort)(0xF00 | MediaDescriptor);
        fat[1] = 0xFFF;
        int nextCluster = 2;
        int rootDirSlot = 0;

        WriteBpb(image);

        foreach (string entryPath in Directory.EnumerateFileSystemEntries(_sourceDirectory)) {
            if (Directory.Exists(entryPath)) {
                string dirName = Path.GetFileName(entryPath);
                string dosDir = ToDosBaseName(dirName);
                if (rootDirSlot >= RootDirEntries) {
                    LogSkipWarning(dirName, "no root directory slots available");
                    continue;
                }
                int dirCluster = nextCluster;
                if (dirCluster >= TotalDataClusters + 2) {
                    LogSkipWarning(dirName, "no clusters available");
                    continue;
                }
                nextCluster++;
                fat[dirCluster] = 0xFFF;
                WriteRootDirEntry(image, rootDirSlot, dosDir, dirCluster, 0, isDirectory: true);
                rootDirSlot++;
                WriteSubdirEntries(image, fat, entryPath, dirCluster, ref nextCluster);
            } else if (File.Exists(entryPath)) {
                string fileName = Path.GetFileName(entryPath);
                byte[] content = File.ReadAllBytes(entryPath);
                if (!TryAllocateAndWriteFile(image, fat, content, ref nextCluster, out int firstCluster)) {
                    LogSkipWarning(fileName, "not enough clusters remaining");
                    continue;
                }
                if (rootDirSlot >= RootDirEntries) {
                    LogSkipWarning(fileName, "no root directory slots available");
                    continue;
                }
                string dosName = ToDosFileName(fileName);
                WriteRootDirEntry(image, rootDirSlot, dosName, firstCluster, content.Length, isDirectory: false);
                rootDirSlot++;
            }
        }

        WriteFatTables(image, fat);
        return image;
    }

    private void WriteSubdirEntries(byte[] image, ushort[] fat, string hostDirPath, int dirCluster, ref int nextCluster) {
        int dirSector = DataStartSector + (dirCluster - 2) * SectorsPerCluster;
        int dirByteOffset = dirSector * BytesPerSector;
        int maxEntriesInCluster = BytesPerSector * SectorsPerCluster / 32;
        int slot = 0;

        foreach (string filePath in Directory.EnumerateFiles(hostDirPath)) {
            if (slot >= maxEntriesInCluster) {
                LogSkipWarning(Path.GetFileName(filePath), "subdirectory cluster full");
                continue;
            }
            byte[] content = File.ReadAllBytes(filePath);
            if (!TryAllocateAndWriteFile(image, fat, content, ref nextCluster, out int firstCluster)) {
                LogSkipWarning(Path.GetFileName(filePath), "not enough clusters remaining");
                continue;
            }
            string dosName = ToDosFileName(Path.GetFileName(filePath));
            WriteDirectoryEntry(image, dirByteOffset + slot * 32, dosName, firstCluster, content.Length, isDirectory: false);
            slot++;
        }
    }

    private static bool TryAllocateAndWriteFile(byte[] image, ushort[] fat, byte[] content, ref int nextCluster, out int firstCluster) {
        int clustersNeeded = content.Length == 0 ? 1 : (content.Length + BytesPerSector * SectorsPerCluster - 1) / (BytesPerSector * SectorsPerCluster);
        if (nextCluster + clustersNeeded - 1 >= TotalDataClusters + 2) {
            firstCluster = 0;
            return false;
        }
        firstCluster = nextCluster;
        for (int i = 0; i < clustersNeeded; i++) {
            int cluster = nextCluster++;
            fat[cluster] = i == clustersNeeded - 1 ? (ushort)0xFFF : (ushort)nextCluster;
        }
        WriteFileData(image, content, firstCluster);
        return true;
    }

    private static void WriteFileData(byte[] image, byte[] content, int firstCluster) {
        int written = 0;
        int cluster = firstCluster;
        while (written < content.Length) {
            int sector = DataStartSector + (cluster - 2) * SectorsPerCluster;
            int byteOffset = sector * BytesPerSector;
            int toCopy = Math.Min(BytesPerSector * SectorsPerCluster, content.Length - written);
            content.AsSpan(written, toCopy).CopyTo(image.AsSpan(byteOffset, toCopy));
            written += toCopy;
            cluster++;
        }
    }

    private static void WriteRootDirEntry(byte[] image, int slot, string dosName, int firstCluster, int fileSize, bool isDirectory) {
        int byteOffset = RootDirStartSector * BytesPerSector + slot * 32;
        WriteDirectoryEntry(image, byteOffset, dosName, firstCluster, fileSize, isDirectory);
    }

    private static void WriteDirectoryEntry(byte[] image, int byteOffset, string dosName, int firstCluster, int fileSize, bool isDirectory) {
        Span<byte> entry = image.AsSpan(byteOffset, 32);
        entry.Clear();
        string[] parts = dosName.Contains('.') ? dosName.Split('.') : new[] { dosName, string.Empty };
        string baseName = parts[0].PadRight(8)[..8];
        string ext = (parts.Length > 1 ? parts[1] : string.Empty).PadRight(3)[..3];
        Encoding.ASCII.GetBytes(baseName).CopyTo(entry);
        Encoding.ASCII.GetBytes(ext).CopyTo(entry.Slice(8));
        entry[11] = isDirectory ? (byte)0x10 : (byte)0x20;
        BitConverter.GetBytes((ushort)firstCluster).CopyTo(entry.Slice(26));
        BitConverter.GetBytes(fileSize).CopyTo(entry.Slice(28));
    }

    private static void WriteFatTables(byte[] image, ushort[] fat) {
        for (int fatIndex = 0; fatIndex < NumberOfFats; fatIndex++) {
            int fatByteOffset = (FatStartSector + fatIndex * SectorsPerFat) * BytesPerSector;
            int fatLimit = fatByteOffset + SectorsPerFat * BytesPerSector;
            for (int n = 0; n < fat.Length; n++) {
                int byteIndex = fatByteOffset + n * 3 / 2;
                if (byteIndex + 1 >= fatLimit) {
                    break;
                }
                ushort value = fat[n];
                if ((n & 1) == 0) {
                    image[byteIndex] = (byte)(value & 0xFF);
                    image[byteIndex + 1] = (byte)((image[byteIndex + 1] & 0xF0) | ((value >> 8) & 0x0F));
                } else {
                    image[byteIndex] = (byte)((image[byteIndex] & 0x0F) | ((value & 0x0F) << 4));
                    image[byteIndex + 1] = (byte)(value >> 4);
                }
            }
            image[fatByteOffset] = MediaDescriptor;
            image[fatByteOffset + 1] = 0xFF;
            image[fatByteOffset + 2] = 0xFF;
        }
    }

    private static void WriteBpb(byte[] image) {
        Span<byte> boot = image.AsSpan(0, BytesPerSector);
        boot[0] = 0xEB;
        boot[1] = 0x3C;
        boot[2] = 0x90;
        Encoding.ASCII.GetBytes("SPICE86 ").CopyTo(boot.Slice(3));
        BitConverter.GetBytes((ushort)BytesPerSector).CopyTo(boot.Slice(11));
        boot[13] = SectorsPerCluster;
        BitConverter.GetBytes((ushort)ReservedSectors).CopyTo(boot.Slice(14));
        boot[16] = NumberOfFats;
        BitConverter.GetBytes((ushort)RootDirEntries).CopyTo(boot.Slice(17));
        BitConverter.GetBytes((ushort)TotalSectors).CopyTo(boot.Slice(19));
        boot[21] = MediaDescriptor;
        BitConverter.GetBytes((ushort)SectorsPerFat).CopyTo(boot.Slice(22));
        BitConverter.GetBytes((ushort)SectorsPerTrack).CopyTo(boot.Slice(24));
        BitConverter.GetBytes((ushort)NumberOfHeads).CopyTo(boot.Slice(26));
        BitConverter.GetBytes(0u).CopyTo(boot.Slice(28));
        BitConverter.GetBytes(0u).CopyTo(boot.Slice(32));
        boot[36] = 0x00;
        boot[37] = 0x00;
        boot[38] = 0x29;
        BitConverter.GetBytes(0x12345678u).CopyTo(boot.Slice(39));
        Encoding.ASCII.GetBytes("VIRT FLOPPY").CopyTo(boot.Slice(43));
        Encoding.ASCII.GetBytes("FAT12   ").CopyTo(boot.Slice(54));
        boot[510] = 0x55;
        boot[511] = 0xAA;
    }

    private static string ToDosBaseName(string name) {
        return name.Length > 8 ? name[..8].ToUpperInvariant() : name.ToUpperInvariant().PadRight(8);
    }

    private static string ToDosFileName(string name) {
        return name.ToUpperInvariant();
    }

    private void LogSkipWarning(string name, string reason) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _loggerService.Warning("VirtualFloppyImage: skipping '{Name}' - {Reason}", name, reason);
        }
    }
}
