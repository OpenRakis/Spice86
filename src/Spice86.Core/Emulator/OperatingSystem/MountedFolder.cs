namespace Spice86.Core.Emulator.OperatingSystem;

/// <summary>
/// Represents a host folder used as a drive by DOS.
/// </summary>
public class MountedFolder {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="fullName">The full host path to the folder to be used as the DOS drive root.</param>
    /// <param name="currentFolder">The full host path used by DOS as the current folder for the drive.</param>
    public MountedFolder(string fullName, string currentFolder) {
        FullName = fullName;
        CurrentFolder = currentFolder;
    }

    /// <summary>
    /// The full host path to the mounted folder. This path serves as the root of the DOS drive.
    /// </summary>
    public string FullName { get; init; }

    /// <summary>
    /// The full host path set by DOS as the current folder for the drive.
    /// </summary>
    public string CurrentFolder { get; set; }
}
