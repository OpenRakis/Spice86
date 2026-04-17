namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Represents an in-memory read-only virtual drive (typically Z: for AUTOEXEC.BAT).
/// </summary>
public class MemoryDrive : DosDriveBase {
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    private static string NormalizePath(string path) => path.Replace('/', '\\');

    /// <summary>
    /// Adds a file to the memory drive.
    /// </summary>
    /// <param name="path">The file path (e.g., "AUTOEXEC.BAT" or "BATCH\SCRIPT.BAT").</param>
    /// <param name="content">The file content as bytes.</param>
    public void AddFile(string path, byte[] content) {
        _files[NormalizePath(path)] = content;
    }

    /// <summary>
    /// Gets a file from the memory drive.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The file content as bytes.</returns>
    /// <exception cref="FileNotFoundException">If file does not exist.</exception>
    public byte[] GetFile(string path) {
        if (_files.TryGetValue(NormalizePath(path), out byte[]? content)) {
            return content;
        }
        throw new FileNotFoundException($"File not found: {path}");
    }

    /// <summary>
    /// Checks if a file exists on the memory drive.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>True if file exists; false otherwise.</returns>
    public bool FileExists(string path) {
        return _files.ContainsKey(NormalizePath(path));
    }

    /// <summary>
    /// Checks if a directory exists on the memory drive.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <returns>True if directory exists; false otherwise.</returns>
    public bool DirectoryExists(string path) {
        string normalizedPath = NormalizePath(path);
        if (!normalizedPath.EndsWith("\\")) {
            normalizedPath += "\\";
        }
        return _files.Keys.Any(f => f.StartsWith(normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a file on the memory drive (not supported - throws exception).
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <exception cref="NotSupportedException">Always thrown; memory drive is read-only.</exception>
    public void CreateFile(string path) {
        throw new NotSupportedException("Memory drives are read-only; file creation not supported.");
    }
}
