namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Collections.Generic;
using System.IO;

/// <summary>Provides DOS-relative file and directory access for a mounted drive.</summary>
public interface IDosPathContent {
    /// <summary>Gets whether a DOS-relative file exists.</summary>
    bool FileExists(string relativePath);

    /// <summary>Gets whether a DOS-relative directory exists.</summary>
    bool DirectoryExists(string relativePath);

    /// <summary>Opens a DOS-relative file for read access.</summary>
    bool TryOpenRead(string relativePath, out Stream? stream);

    /// <summary>Lists the immediate entries in a DOS-relative directory.</summary>
    IReadOnlyList<DosContentEntry> GetDirectoryEntries(string relativePath);
}
