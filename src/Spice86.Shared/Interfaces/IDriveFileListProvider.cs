namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Storage;

using System.Collections.Generic;

/// <summary>
/// Provides the DOS-visible file and directory listing for a mounted drive,
/// for display in the drive info window.
/// </summary>
public interface IDriveFileListProvider {
    /// <summary>
    /// Tries to obtain the top-level file and directory entries for the specified drive letter,
    /// as they appear to DOS programs.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter (case-insensitive).</param>
    /// <param name="entries">
    /// When this method returns <see langword="true"/>, contains the root-level entries
    /// with children populated for directories.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the listing could be produced; otherwise <see langword="false"/>.
    /// </returns>
    bool TryGetFileList(char driveLetter, out IReadOnlyList<DriveFileEntry>? entries);
}
