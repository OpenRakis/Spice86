namespace Spice86.Core.Emulator.OperatingSystem.Structures;
/// <summary>
/// Base class for all DOS Drives
/// </summary>
public abstract class DosDriveBase {
    /// <summary>
    /// Gets or sets the DOS label.
    /// </summary>
    /// <remarks>11 ASCII encoded characters limit.</remarks>
    public string Label { get; set; } = nameof(Spice86);

    /// <summary>
    /// Gets if the virtual drive can be unmounted (in MS-DOS, MSCDEX can do it)
    /// </summary>
    public bool IsRemovable { get; protected set; }

    /// <summary>
    /// Gets if the media is read only (ie. CD-ROM)
    /// </summary>
    public bool IsReadOnlyMedium { get; }

    /// <summary>
    /// Gets the assigned DOS drive letter.
    /// </summary>
    public required char DriveLetter { get; init; }

    /// <summary>
    /// Gets the DOS assigned drive letter, with a volume separator character appended to it.
    /// </summary>
    public string DosVolume => $"{DriveLetter}{DosPathResolver.VolumeSeparatorChar}";

    /// <summary>
    /// Gets if it is a network drive. Not supported, always <see langword="false" />
    /// </summary>
    public bool IsRemote { get; }
}
