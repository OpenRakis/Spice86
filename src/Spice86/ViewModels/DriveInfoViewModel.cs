namespace Spice86.ViewModels;

using Spice86.Shared.Emulator.Storage;
using Spice86.ViewModels.DataModels;

using System.Collections.ObjectModel;

/// <summary>
/// View model for the drive info window, holding disk layout and DOS file tree data.
/// </summary>
public sealed class DriveInfoViewModel {
    /// <summary>Gets the drive letter displayed in the window title.</summary>
    public char DriveLetter { get; }

    /// <summary>Gets the on-disk content layout snapshot.</summary>
    public DriveContentMap ContentMap { get; }

    /// <summary>Gets whether the content map was available from the drive provider.</summary>
    public bool HasContentMap { get; }

    /// <summary>Gets the root-level DOS file/directory entries for display in the Files tab.</summary>
    public ObservableCollection<FileTreeNode> FileTree { get; }

    /// <summary>Gets a value indicating whether the file listing is available.</summary>
    public bool HasFileTree => FileTree.Count > 0;

    /// <summary>Initialises a new <see cref="DriveInfoViewModel"/>.</summary>
    /// <param name="driveLetter">The DOS drive letter.</param>
    /// <param name="contentMap">The on-disk content layout snapshot.</param>
    /// <param name="hasContentMap">Whether the content map came from the provider.</param>
    /// <param name="fileTree">The root-level DOS file entries.</param>
    public DriveInfoViewModel(char driveLetter, DriveContentMap contentMap, bool hasContentMap, ObservableCollection<FileTreeNode> fileTree) {
        DriveLetter = driveLetter;
        ContentMap = contentMap;
        HasContentMap = hasContentMap;
        FileTree = fileTree;
    }
}