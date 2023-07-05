namespace Spice86.Core.Emulator.OperatingSystem;

/// <summary>
/// Represents a host folder used as a drive by DOS.
/// </summary>
public class MountedFolder {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="driveLetter">The DOS driver letter.</param>
    /// <param name="fullName">The full host path to the folder to be used as the DOS drive root.</param>
    public MountedFolder(char driveLetter, string fullName) {
        DriveLetter = driveLetter;
        MountPoint = fullName;
        CurrentDirectory = "";
    }

    /// <summary>
    /// Gets the DOS drive letter.
    /// </summary>
    public char DriveLetter { get; init; }

    /// <summary>
    /// The full host path to the mounted folder. This path serves as the root of the DOS drive.
    /// </summary>
    public string MountPoint { get; init; }

    /// <summary>
    /// The current directory in use on the drive. Relative to the <see cref="MountPoint"/>
    /// </summary>
    public string CurrentDirectory { get; set; }

    /// <summary>
    /// The full host path. Combined from <see cref="MountPoint"/> and <see cref="CurrentDirectory"/>
    /// </summary>
    public string FullName => Path.Combine(MountPoint, CurrentDirectory);
}
