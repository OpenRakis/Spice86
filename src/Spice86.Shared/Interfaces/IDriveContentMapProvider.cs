namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Storage;

/// <summary>
/// Provides on-demand snapshots of the content layout of a mounted DOS drive
/// for visual representation in the UI.
/// </summary>
public interface IDriveContentMapProvider {
    /// <summary>
    /// Gets a content map for the specified drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter (case-insensitive).</param>
    /// <returns>The content map for the drive.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the specified drive does not expose a content map.</exception>
    DriveContentMap GetContentMap(char driveLetter);
}
