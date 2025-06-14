namespace Spice86.Core.Emulator.OperatingSystem.Structures;
public interface IVirtualDrive {
    public string? Label { get; set; }

    /// <summary>
    /// Gets if the virtual drive can be unmounted (in MS-DOS, MSCDEX can do it)
    /// </summary>
    public bool IsRemovable { get; }

    /// <summary>
    /// Gets if the media is read only (ie. CD-ROM)
    /// </summary>
    public bool IsReadOnlyMedium { get; }

    /// <summary>
    /// Gets the assigned DOS drive letter.
    /// </summary>
    public char DriveLetter { get; }

    /// <summary>
    /// The full host path to the mounted folder. This path serves as the root of the DOS drive.
    /// </summary>
    public string MountedHostDirectory { get; init; }

    /// <summary>
    /// The current DOS directory in use on the drive.
    /// </summary>
    public string CurrentDosDirectory { get; set; }

    /// <summary>
    /// Gets the DOS drive root path.
    /// </summary>
    public string DosDriveRootPath { get; }
}
