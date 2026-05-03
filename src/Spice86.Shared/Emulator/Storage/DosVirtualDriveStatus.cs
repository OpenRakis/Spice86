namespace Spice86.Shared.Emulator.Storage;

/// <summary>
/// Immutable snapshot of a single DOS drive's status, suitable for polling by the UI.
/// </summary>
public sealed class DosVirtualDriveStatus {
    /// <summary>Gets the DOS drive letter (e.g. 'A', 'C', 'D').</summary>
    public char DriveLetter { get; }

    /// <summary>Gets the type of media attached to this drive.</summary>
    public DosVirtualDriveType DriveType { get; }

    /// <summary>Gets whether removable media is currently inserted in the drive.</summary>
    public bool HasMedia { get; }

    /// <summary>Gets the volume label of the mounted media, or an empty string if no media or label.</summary>
    public string VolumeLabel { get; }

    /// <summary>
    /// Gets the file-system path of the currently active disc image, or an empty string when the drive
    /// is backed by a host folder or has no media.
    /// </summary>
    public string CurrentImagePath { get; }

    /// <summary>
    /// Gets the total number of disc images registered for this drive.
    /// A value greater than 1 means Ctrl-F4 disc switching is available.
    /// </summary>
    public int ImageCount { get; }

    /// <summary>
    /// Gets the file name (without directory path) of the currently active disc image,
    /// or an empty string when the drive is backed by a host folder or has no media.
    /// </summary>
    public string CurrentImageFileName => string.IsNullOrEmpty(CurrentImagePath)
        ? string.Empty
        : System.IO.Path.GetFileName(CurrentImagePath);

    /// <summary>
    /// Gets <see langword="true"/> when the drive has more than one registered image,
    /// meaning that Ctrl-F4 disc switching is available.
    /// </summary>
    public bool HasMultipleImages => ImageCount > 1;

    /// <summary>Initialises a new snapshot for a drive.</summary>
    /// <param name="driveLetter">The drive letter.</param>
    /// <param name="driveType">The drive media type.</param>
    /// <param name="hasMedia">Whether media is inserted.</param>
    /// <param name="volumeLabel">The volume label (may be empty).</param>
    /// <param name="currentImagePath">The path of the active disc image (empty for folder-backed drives).</param>
    /// <param name="imageCount">Total number of disc images registered for this drive.</param>
    public DosVirtualDriveStatus(char driveLetter, DosVirtualDriveType driveType, bool hasMedia, string volumeLabel,
        string currentImagePath = "", int imageCount = 0) {
        DriveLetter = driveLetter;
        DriveType = driveType;
        HasMedia = hasMedia;
        VolumeLabel = volumeLabel;
        CurrentImagePath = currentImagePath;
        ImageCount = imageCount;
    }
}
