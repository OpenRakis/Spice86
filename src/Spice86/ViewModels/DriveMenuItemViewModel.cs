namespace Spice86.ViewModels;

using Avalonia.Platform.Storage;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

/// <summary>
/// View model for a single drive entry in the Drives menu, exposing a combobox with available images.
/// </summary>
public sealed partial class DriveMenuItemViewModel : ObservableObject {
    private readonly IDiscSwapper _discSwapper;
    private readonly IDriveMountService _mountService;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly List<string> _allImagePaths;

    /// <summary>Gets the drive letter for this entry.</summary>
    public char DriveLetter { get; }

    /// <summary>Gets the drive type for this entry.</summary>
    public DosVirtualDriveType DriveType { get; }

    /// <summary>Gets the options shown in the combobox (image file names plus "...").</summary>
    public ObservableCollection<string> ComboboxOptions { get; } = new();

    [ObservableProperty]
    private string _selectedOption = string.Empty;

    private bool _suppressSelectionHandling;

    /// <summary>
    /// Initialises a new <see cref="DriveMenuItemViewModel"/>.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter.</param>
    /// <param name="driveType">The drive type.</param>
    /// <param name="allImagePaths">All registered image paths for this drive.</param>
    /// <param name="currentImagePath">The currently active image path.</param>
    /// <param name="discSwapper">The disc swapper service.</param>
    /// <param name="mountService">The drive mount service.</param>
    /// <param name="hostStorageProvider">The host storage provider for file picker dialogs.</param>
    public DriveMenuItemViewModel(
        char driveLetter,
        DosVirtualDriveType driveType,
        IReadOnlyList<string> allImagePaths,
        string currentImagePath,
        IDiscSwapper discSwapper,
        IDriveMountService mountService,
        IHostStorageProvider hostStorageProvider) {
        DriveLetter = driveLetter;
        DriveType = driveType;
        _discSwapper = discSwapper;
        _mountService = mountService;
        _hostStorageProvider = hostStorageProvider;
        _allImagePaths = new List<string>(allImagePaths);
        RebuildOptions(currentImagePath);
    }

    private void RebuildOptions(string currentImagePath) {
        _suppressSelectionHandling = true;
        ComboboxOptions.Clear();
        foreach (string path in _allImagePaths) {
            ComboboxOptions.Add(Path.GetFileName(path));
        }
        ComboboxOptions.Add("...");
        string currentFileName;
        if (string.IsNullOrEmpty(currentImagePath)) {
            currentFileName = string.Empty;
        } else {
            currentFileName = Path.GetFileName(currentImagePath);
        }
        if (ComboboxOptions.Contains(currentFileName)) {
            SelectedOption = currentFileName;
        } else if (ComboboxOptions.Count > 1) {
            SelectedOption = ComboboxOptions[0];
        } else {
            SelectedOption = string.Empty;
        }
        _suppressSelectionHandling = false;
    }

    /// <summary>Updates the drive data from a new status snapshot.</summary>
    /// <param name="status">The latest drive status snapshot.</param>
    public void UpdateFromStatus(DosVirtualDriveStatus status) {
        bool pathsChanged = status.AllImagePaths.Count != _allImagePaths.Count;
        if (!pathsChanged) {
            for (int i = 0; i < _allImagePaths.Count; i++) {
                if (!string.Equals(_allImagePaths[i], status.AllImagePaths[i], StringComparison.Ordinal)) {
                    pathsChanged = true;
                    break;
                }
            }
        }
        if (pathsChanged) {
            _allImagePaths.Clear();
            foreach (string path in status.AllImagePaths) {
                _allImagePaths.Add(path);
            }
        }
        RebuildOptions(status.CurrentImagePath);
    }

    partial void OnSelectedOptionChanged(string value) {
        if (_suppressSelectionHandling) {
            return;
        }
        if (string.IsNullOrEmpty(value)) {
            return;
        }
        if (string.Equals(value, "...", StringComparison.Ordinal)) {
            OpenMountDialog();
            return;
        }
        int index = ComboboxOptions.IndexOf(value);
        if (index >= 0 && index < _allImagePaths.Count) {
            _discSwapper.SwapToImageIndex(DriveLetter, index);
        }
    }

    private async void OpenMountDialog() {
        FilePickerOpenOptions options = new() {
            Title = $"Select image for drive {DriveLetter}:",
            AllowMultiple = false,
        };
        IReadOnlyList<IStorageFile> files = await _hostStorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0) {
            return;
        }
        string? path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) {
            return;
        }
        if (DriveType == DosVirtualDriveType.Floppy) {
            _mountService.MountImageAsFloppy(DriveLetter, path);
        } else {
            _mountService.MountImageAsCdRom(DriveLetter, path);
        }
    }
}
