namespace Spice86.Tests.AsmFixtures;

/// <summary>
/// Records the files a run created or changed on its C: drive. There is no emulator seam for
/// drive files, so the recorder snapshots every file of the mounted host folder at construction,
/// before the program starts, and lists on demand the files that are new or whose bytes differ
/// from that snapshot, sorted by path. Fixture files that did not change are left out. With no
/// C: drive the list is empty.
/// </summary>
internal sealed class DriveFileRecorder {
    private readonly string? _driveDirectory;
    private readonly Dictionary<string, byte[]> _snapshot;

    public DriveFileRecorder(string? driveDirectory) {
        _driveDirectory = driveDirectory;
        _snapshot = driveDirectory is null ? new() : Snapshot(driveDirectory);
    }

    /// <summary>The relative '/'-separated path and content of every file the run created or
    /// changed on the C: drive, sorted by path ordinal.</summary>
    public IReadOnlyList<(string Path, byte[] Content)> WrittenFiles {
        get {
            if (_driveDirectory is null) {
                return [];
            }
            List<(string Path, byte[] Content)> written = new();
            foreach ((string path, byte[] content) in Snapshot(_driveDirectory)) {
                if (!_snapshot.TryGetValue(path, out byte[]? before) || !before.SequenceEqual(content)) {
                    written.Add((path, content));
                }
            }
            return written;
        }
    }

    private static Dictionary<string, byte[]> Snapshot(string driveDirectory) {
        Dictionary<string, byte[]> snapshot = new(StringComparer.Ordinal);
        foreach (string filePath in Directory.EnumerateFiles(driveDirectory, "*", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(driveDirectory, filePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            snapshot[relativePath] = File.ReadAllBytes(filePath);
        }
        return snapshot.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
