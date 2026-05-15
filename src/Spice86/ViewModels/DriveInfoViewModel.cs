namespace Spice86.ViewModels;

using Spice86.Shared.Emulator.Storage;

using System.Collections.ObjectModel;

/// <summary>
/// View model for the drive info window, holding disk layout and DOS file tree data.
/// </summary>
public sealed class DriveInfoViewModel {
    /// <summary>Gets the drive letter displayed in the window title.</summary>
    public char DriveLetter { get; }

    /// <summary>Gets the on-disk content layout snapshot, if available.</summary>
    public DriveContentMap? ContentMap { get; }

    /// <summary>Gets the root-level DOS file/directory entries for display in the Files tab.</summary>
    public ObservableCollection<FileTreeNode> FileTree { get; }

    /// <summary>Gets a value indicating whether the file listing is available.</summary>
    public bool HasFileTree => FileTree.Count > 0;

    /// <summary>Initialises a new <see cref="DriveInfoViewModel"/>.</summary>
    /// <param name="driveLetter">The DOS drive letter.</param>
    /// <param name="contentMap">The on-disk content layout snapshot, or <see langword="null"/>.</param>
    /// <param name="fileTree">The root-level DOS file entries.</param>
    public DriveInfoViewModel(char driveLetter, DriveContentMap? contentMap, ObservableCollection<FileTreeNode> fileTree) {
        DriveLetter = driveLetter;
        ContentMap = contentMap;
        FileTree = fileTree;
    }
}