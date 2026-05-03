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

    /// <summary>Initialises a new snapshot for a drive.</summary>
    /// <param name="driveLetter">The drive letter.</param>
    /// <param name="driveType">The drive media type.</param>
    /// <param name="hasMedia">Whether media is inserted.</param>
    /// <param name="volumeLabel">The volume label (may be empty).</param>
    public DosVirtualDriveStatus(char driveLetter, DosVirtualDriveType driveType, bool hasMedia, string volumeLabel) {
        DriveLetter = driveLetter;
        DriveType = driveType;
        HasMedia = hasMedia;
        VolumeLabel = volumeLabel;
    }
}
