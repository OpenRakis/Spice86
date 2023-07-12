namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Shared.Utils;

/// <summary>
/// Represents a host folder used as a drive by DOS.
/// </summary>
public class MountedFolder {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="driveLetter">The DOS driver letter.</param>
    /// <param name="mountedHostDirectory">The full host path to the folder to be used as the DOS drive root.</param>
    public MountedFolder(char driveLetter, string mountedHostDirectory) {
        DriveLetter = driveLetter;
        MountedHostDirectory = mountedHostDirectory;
        FullHostCurrentDirectory = "";
    }

    /// <summary>
    /// Gets the DOS drive letter.
    /// </summary>
    public char DriveLetter { get; init; }

    /// <summary>
    /// Gets the DOS drive root path.
    /// </summary>
    public string DosDriveRootPath => $"{DriveLetter}{DosPathResolver.VolumeSeparatorChar}";

    /// <summary>
    /// The full host path to the mounted folder. This path serves as the root of the DOS drive.
    /// </summary>
    public string MountedHostDirectory { get; init; }

    /// <summary>
    /// The full path to the current host directory in use on the drive.
    /// </summary>
    public string FullHostCurrentDirectory { get; set; }

    /// <summary>
    /// The full path to the current DOS directory in use on the drive.
    /// </summary>
    public string FullDosCurrentDirectory => $"{DosDriveRootPath}{DosPathResolver.DirectorySeparatorChar}{ConvertUtils.ToBackSlashPath(FullHostCurrentDirectory[MountedHostDirectory.Length..])}".ToUpperInvariant();
}
