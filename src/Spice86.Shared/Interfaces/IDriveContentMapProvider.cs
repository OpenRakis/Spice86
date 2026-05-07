namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Storage;

/// <summary>
/// Provides on-demand snapshots of the content layout of a mounted DOS drive
/// for visual representation in the UI.
/// </summary>
public interface IDriveContentMapProvider {
    /// <summary>
    /// Tries to obtain a content map for the specified drive letter.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter (case-insensitive).</param>
    /// <param name="map">When this method returns <see langword="true"/>, contains the content map.</param>
    /// <returns><see langword="true"/> if a content map could be produced; otherwise <see langword="false"/>.</returns>
    bool TryGetContentMap(char driveLetter, out DriveContentMap? map);
}
