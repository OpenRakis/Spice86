namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

using System.Linq;

/// <summary>Represents a DOS drive backed by a host folder.</summary>
public sealed class FolderDrive : DosDriveBase, IDosPathContent {
    /// <summary>Gets the host folder that is the root of the DOS drive.</summary>
    public required string MountedHostDirectory { get; init; }

    public bool FileExists(string relativePath) {
        return TryResolveHostPath(relativePath, out string? path) && File.Exists(path);
    }

    public bool DirectoryExists(string relativePath) {
        return TryResolveHostPath(relativePath, out string? path) && Directory.Exists(path);
    }

    public bool TryOpenRead(string relativePath, out Stream? stream) {
        stream = null;
        if (!TryResolveHostPath(relativePath, out string? path) || !File.Exists(path)) {
            return false;
        }
        stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return true;
    }

    public IReadOnlyList<DosContentEntry> GetDirectoryEntries(string relativePath) {
        if (!TryResolveHostPath(relativePath, out string? path) || !Directory.Exists(path)) {
            return Array.Empty<DosContentEntry>();
        }

        List<DosContentEntry> entries = new();
        foreach (string directoryPath in Directory.EnumerateDirectories(path)) {
            DirectoryInfo directory = new(directoryPath);
            entries.Add(new DosContentEntry(directory.Name, true, 0,
                (DosFileAttributes)directory.Attributes, directory.CreationTimeUtc, directory.FullName));
        }
        foreach (string filePath in Directory.EnumerateFiles(path)) {
            FileInfo file = new(filePath);
            entries.Add(new DosContentEntry(file.Name, false, (uint)file.Length,
                (DosFileAttributes)file.Attributes, file.CreationTimeUtc, file.FullName));
        }
        return entries;
    }

    private bool TryResolveHostPath(string relativePath, out string? path) {
        path = null;
        if (string.IsNullOrWhiteSpace(MountedHostDirectory)) {
            return false;
        }

        string current = MountedHostDirectory;
        string[] parts = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++) {
            DirectoryInfo directory = new(current);
            DirectoryInfo? nextDirectory = directory.EnumerateDirectories()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, parts[i], StringComparison.OrdinalIgnoreCase));
            if (i < parts.Length - 1) {
                if (nextDirectory is null) {
                    return false;
                }
                current = nextDirectory.FullName;
                continue;
            }

            if (nextDirectory is not null) {
                path = nextDirectory.FullName;
                return true;
            }

            FileInfo? file = directory.EnumerateFiles()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, parts[i], StringComparison.OrdinalIgnoreCase));
            if (file is null) {
                return false;
            }
            path = file.FullName;
            return true;
        }

        path = current;
        return true;
    }
}