namespace Spice86.Core.Emulator.OperatingSystem;

internal class MountedFolder {
    public MountedFolder(string fullName, string currentFolder) {
        FullName = fullName;
        CurrentFolder = currentFolder;
    }

    /// <summary>
    /// The full host path to the mounted folder
    /// </summary>
    public string FullName { get; init; }

    /// <summary>
    /// The full path to the current folder in use by DOS
    /// </summary>
    public string CurrentFolder { get; set; }
}
