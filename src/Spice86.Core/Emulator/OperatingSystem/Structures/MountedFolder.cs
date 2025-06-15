namespace Spice86.Core.Emulator.OperatingSystem.Structures;

/// <inheritdoc cref="IVirtualDrive" />
public class MountedFolder : IVirtualDrive {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="driveLetter">The DOS driver letter.</param>
    /// <param name="mountedHostDirectory">The full host path to the folder to be used as the DOS drive root.</param>
    public MountedFolder(char driveLetter, string mountedHostDirectory) {
        DriveLetter = driveLetter;
        MountedHostDirectory = mountedHostDirectory;
        CurrentDosDirectory = "";
    }

    public char DriveLetter { get; init; }

    public string DosVolume => $"{DriveLetter}{DosPathResolver.VolumeSeparatorChar}";

    public string MountedHostDirectory { get; init; }

    public string CurrentDosDirectory { get; set; }

    public string? Label { get; set; }

    public bool IsRemovable => false;

    public bool IsReadOnlyMedium => false;
}
