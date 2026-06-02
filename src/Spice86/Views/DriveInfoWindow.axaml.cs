namespace Spice86.Views;

using Avalonia.Controls;

using Spice86.ViewModels.DataModels;
using Spice86.ViewModels;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

/// <summary>
/// Window that shows disk representation and DOS-visible file listing for a single drive.
/// </summary>
public partial class DriveInfoWindow : Window {
    /// <summary>Initialises a new <see cref="DriveInfoWindow"/>.</summary>
    public DriveInfoWindow() {
        InitializeComponent();
    }

    /// <summary>
    /// Builds the view model from the drive data and opens the window as a dialog.
    /// </summary>
    /// <param name="owner">The parent window.</param>
    /// <param name="driveVm">The drive menu item that was clicked.</param>
    public static async Task ShowForDrive(Window owner, DriveMenuItemViewModel driveVm) {
        ObservableCollection<FileTreeNode> fileTree = BuildFileTree(driveVm);
        DriveInfoViewModel vm = new DriveInfoViewModel(driveVm.DriveLetter, driveVm.ContentMap, fileTree);
        DriveInfoWindow window = new DriveInfoWindow {
            DataContext = vm
        };
        await window.ShowDialog(owner);
    }

    private static ObservableCollection<FileTreeNode> BuildFileTree(DriveMenuItemViewModel driveVm) {
        if (driveVm.FileListProvider == null) {
            return new ObservableCollection<FileTreeNode>();
        }

        IReadOnlyList<Spice86.Shared.Emulator.Storage.DriveFileEntry> entries = driveVm.FileListProvider.GetFileList(driveVm.DriveLetter);
        return BuildNodes(entries);
    }

    private static ObservableCollection<FileTreeNode> BuildNodes(IReadOnlyList<Spice86.Shared.Emulator.Storage.DriveFileEntry> entries) {
        ObservableCollection<FileTreeNode> result = new ObservableCollection<FileTreeNode>();
        for (int i = 0; i < entries.Count; i++) {
            Spice86.Shared.Emulator.Storage.DriveFileEntry entry = entries[i];
            ObservableCollection<FileTreeNode> children = BuildNodes(entry.Children);
            string sizeText = entry.IsDirectory ? string.Empty : FormatSize(entry.Size);
            result.Add(new FileTreeNode(entry.Name, sizeText, entry.Attributes, entry.IsDirectory, children));
        }
        return result;
    }

    private static string FormatSize(long bytes) {
        if (bytes < 1024) {
            return $"{bytes} B";
        }
        if (bytes < 1024 * 1024) {
            return $"{bytes / 1024} KB";
        }
        return $"{bytes / (1024 * 1024)} MB";
    }
}