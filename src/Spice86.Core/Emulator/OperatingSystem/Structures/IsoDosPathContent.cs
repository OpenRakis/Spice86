namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Emulator.Storage.CdRom;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>Provides DOS file access over an ISO9660 CD image.</summary>
internal sealed class IsoDosPathContent : IDosPathContent {
    private readonly ICdRomDrive _drive;

    public IsoDosPathContent(ICdRomDrive drive) {
        _drive = drive;
    }

    public bool FileExists(string relativePath) {
        return TryFind(relativePath, out IsoDirectoryRecord? record) && record is not null && !record.IsDirectory;
    }

    public bool DirectoryExists(string relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return true;
        }
        return TryFind(relativePath, out IsoDirectoryRecord? record) && record is not null && record.IsDirectory;
    }

    public bool TryOpenRead(string relativePath, out Stream? stream) {
        stream = null;
        if (!TryFind(relativePath, out IsoDirectoryRecord? record) || record is null || record.IsDirectory) {
            return false;
        }

        int sectorSize = _drive.Image.PrimaryVolume.LogicalBlockSize;
        if (sectorSize <= 0) {
            sectorSize = 2048;
        }
        byte[] data = new byte[record.DataLength];
        int sectors = (data.Length + sectorSize - 1) / sectorSize;
        byte[] sectorBuffer = new byte[sectors * sectorSize];
        int bytesRead = _drive.Read(record.ExtentLba, sectors, sectorBuffer, CdSectorMode.CookedData2048);
        if (bytesRead < data.Length) {
            return false;
        }
        Array.Copy(sectorBuffer, data, data.Length);
        stream = new MemoryStream(data, writable: false);
        return true;
    }

    public IReadOnlyList<DosContentEntry> GetDirectoryEntries(string relativePath) {
        if (!TryFind(relativePath, out IsoDirectoryRecord? directory) || directory is null || !directory.IsDirectory) {
            return Array.Empty<DosContentEntry>();
        }

        IReadOnlyList<IsoDirectoryRecord> records = ReadDirectoryRecords(directory.ExtentLba, directory.DataLength);
        List<DosContentEntry> entries = new(records.Count);
        for (int i = 0; i < records.Count; i++) {
            IsoDirectoryRecord record = records[i];
            if (record.Name is "\x00" or "\x01") {
                continue;
            }
            DosFileAttributes attributes = record.IsDirectory
                ? DosFileAttributes.Directory
                : DosFileAttributes.ReadOnly;
            entries.Add(new DosContentEntry(NormalizeName(record.Name), record.IsDirectory,
                (uint)record.DataLength, attributes, DateTime.UnixEpoch, null));
        }
        return entries;
    }

    internal bool TryGetRecord(string relativePath, out IsoDirectoryRecord? record) {
        return TryFind(relativePath, out record);
    }

    private bool TryFind(string relativePath, out IsoDirectoryRecord? result) {
        result = null;
        string[] parts = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int directoryLba = _drive.Image.PrimaryVolume.RootDirectoryLba;
        int directorySize = _drive.Image.PrimaryVolume.RootDirectorySize;
        if (parts.Length == 0) {
            result = new IsoDirectoryRecord("", directoryLba, directorySize, true, 2);
            return true;
        }

        for (int i = 0; i < parts.Length; i++) {
            IsoDirectoryRecord? match = FindInDirectory(directoryLba, directorySize, parts[i]);
            if (match is null) {
                return false;
            }
            if (i == parts.Length - 1) {
                result = match;
                return true;
            }
            if (!match.IsDirectory) {
                return false;
            }
            directoryLba = match.ExtentLba;
            directorySize = match.DataLength;
        }

        return false;
    }

    private IsoDirectoryRecord? FindInDirectory(int directoryLba, int directorySize, string requestedName) {
        IReadOnlyList<IsoDirectoryRecord> records = ReadDirectoryRecords(directoryLba, directorySize);
        for (int i = 0; i < records.Count; i++) {
            IsoDirectoryRecord record = records[i];
            if (record.Name is not ("\x00" or "\x01") &&
                string.Equals(NormalizeName(record.Name), NormalizeName(requestedName), StringComparison.OrdinalIgnoreCase)) {
                return record;
            }
        }
        return null;
    }

    private IReadOnlyList<IsoDirectoryRecord> ReadDirectoryRecords(int directoryLba, int directorySize) {
        List<IsoDirectoryRecord> records = new();
        int sectorSize = _drive.Image.PrimaryVolume.LogicalBlockSize;
        if (sectorSize <= 0) {
            sectorSize = 2048;
        }
        int sectors = (directorySize + sectorSize - 1) / sectorSize;
        byte[] buffer = new byte[sectorSize];
        for (int sector = 0; sector < sectors; sector++) {
            int bytesRead = _drive.Read(directoryLba + sector, 1, buffer, CdSectorMode.CookedData2048);
            if (bytesRead < sectorSize) {
                return records;
            }
            int offset = 0;
            while (offset < buffer.Length && buffer[offset] != 0) {
                int recordLength = buffer[offset];
                if (recordLength <= 0 || offset + recordLength > buffer.Length) {
                    break;
                }
                IsoDirectoryRecord? record = IsoDirectoryRecord.ParseNullable(buffer.AsSpan(offset, recordLength));
                if (record is not null) {
                    records.Add(record);
                }
                offset += recordLength;
            }
        }
        return records;
    }

    private static string NormalizeName(string name) {
        int semicolon = name.IndexOf(';');
        if (semicolon >= 0) {
            name = name[..semicolon];
        }
        return name.TrimEnd('.');
    }
}
