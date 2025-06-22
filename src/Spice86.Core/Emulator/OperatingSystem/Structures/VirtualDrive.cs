namespace Spice86.Core.Emulator.OperatingSystem.Structures;
/// <summary>
/// Represents a host folder used as a drive by DOS.
/// </summary>
public class VirtualDrive : DosDriveBase {
    /// <summary>
    /// The full host path to the mounted folder. This path serves as the root of the DOS drive.
    /// </summary>
    public required string MountedHostDirectory { get; init; }

    /// <summary>
    /// Gets the absolute path to the current DOS directory in use on the drive.
    /// </summary>
    public required string CurrentDosDirectory { get; set; }
}
