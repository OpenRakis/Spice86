namespace Spice86.Core.Emulator.OperatingSystem.Structures;
/// <summary>
/// Represents a host folder used as a drive by DOS.
/// </summary>
public class VirtualDrive {
    /// <summary>
    /// Gets or sets the DOS label.
    /// </summary>
    /// <remarks>11 ASCII encoded characters limit.</remarks>
    public string Label { get; set; } = "Spice86";

    /// <summary>
    /// Gets if the virtual drive can be unmounted (in MS-DOS, MSCDEX can do it)
    /// </summary>
    public virtual bool IsRemovable { get; }

    /// <summary>
    /// Gets if the media is read only (ie. CD-ROM)
    /// </summary>
    public virtual bool IsReadOnlyMedium { get; }

    /// <summary>
    /// Gets the assigned DOS drive letter.
    /// </summary>
    public required char DriveLetter { get; init; }

    /// <summary>
    /// The full host path to the mounted folder. This path serves as the root of the DOS drive.
    /// </summary>
    public required string MountedHostDirectory { get; init; }

    /// <summary>
    /// Gets the absolute path to the current DOS directory in use on the drive.
    /// </summary>
    public required string CurrentDosDirectory { get; set; }

    /// <summary>
    /// Gets the DOS assigned drive letter, with a volume separator character appended to it.
    /// </summary>
    public string DosVolume => $"{DriveLetter}{DosPathResolver.VolumeSeparatorChar}";

    /// <summary>
    /// Gets if it is a network drive. Not supported, always <see langword="false" />
    /// </summary>
    public virtual bool IsRemote { get; }
}
