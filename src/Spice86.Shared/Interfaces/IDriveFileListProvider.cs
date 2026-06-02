namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Storage;

using System.Collections.Generic;

/// <summary>
/// Provides the DOS-visible file and directory listing for a mounted drive,
/// for display in the drive info window.
/// </summary>
public interface IDriveFileListProvider {
    /// <summary>
    /// Gets the top-level file and directory entries for the specified drive letter,
    /// as they appear to DOS programs.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter (case-insensitive).</param>
    /// <returns>The root-level entries with children populated for directories.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the specified drive does not expose a DOS-visible file list.</exception>
    IReadOnlyList<DriveFileEntry> GetFileList(char driveLetter);
}
