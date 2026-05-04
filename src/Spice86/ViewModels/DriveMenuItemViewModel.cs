namespace Spice86.ViewModels;

using Avalonia.Platform.Storage;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// View model for a single drive entry in the Drives menu, exposing a combobox with available images
/// and buttons to mount a folder or image file.
/// </summary>
public sealed partial class DriveMenuItemViewModel : ObservableObject {
    private readonly IDiscSwapper _discSwapper;
    private readonly IDriveMountService _mountService;
    private readonly IHostStorageProvider _hostStorageProvider;
    private readonly IDriveEventNotifier _driveEventNotifier;
    private readonly List<string> _allImagePaths;

    /// <summary>Gets the drive letter for this entry.</summary>
    public char DriveLetter { get; }

    /// <summary>Gets the drive type for this entry.</summary>
    public DosVirtualDriveType DriveType { get; }

    /// <summary>Gets whether this drive is a floppy drive (for icon visibility binding).</summary>
    public bool IsFloppy => DriveType == DosVirtualDriveType.Floppy;

    /// <summary>Gets whether this drive is a CD-ROM drive (for icon visibility binding).</summary>
    public bool IsCdRom => DriveType == DosVirtualDriveType.CdRom;

    /// <summary>Gets whether this drive is a hard disk drive (combobox disabled, no mount buttons).</summary>
    public bool IsHdd => DriveType == DosVirtualDriveType.Fixed;

    /// <summary>Gets the volume label of the currently active media, or empty when no media is present.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipText))]
    private string _volumeLabel = string.Empty;

    /// <summary>Gets a human-readable tooltip summarising this drive's current state.</summary>
    public string TooltipText {
        get {
            string typeName;
            if (DriveType == DosVirtualDriveType.Floppy) {
                typeName = "Floppy";
            } else if (DriveType == DosVirtualDriveType.CdRom) {
                typeName = "CD-ROM";
            } else {
                typeName = "Hard Disk";
            }
            string label = string.IsNullOrEmpty(VolumeLabel) ? "(no label)" : VolumeLabel;
            if (string.IsNullOrEmpty(SelectedOption)) {
                return $"{DriveLetter}: {typeName} — {label} — no media";
            }
            return $"{DriveLetter}: {typeName} — {label} — {SelectedOption}";
        }
    }

    /// <summary>Gets the options shown in the combobox (image file names for floppy/CD drives).</summary>
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
    /// <param name="volumeLabel">The volume label of the mounted media.</param>
    /// <param name="discSwapper">The disc swapper service.</param>
    /// <param name="mountService">The drive mount service.</param>
    /// <param name="hostStorageProvider">The host storage provider for file picker dialogs.</param>
    /// <param name="driveEventNotifier">The notifier used to surface mount errors as toast notifications.</param>
    public DriveMenuItemViewModel(
        char driveLetter,
        DosVirtualDriveType driveType,
        IReadOnlyList<string> allImagePaths,
        string currentImagePath,
        string volumeLabel,
        IDiscSwapper discSwapper,
        IDriveMountService mountService,
        IHostStorageProvider hostStorageProvider,
        IDriveEventNotifier driveEventNotifier) {
        DriveLetter = driveLetter;
        DriveType = driveType;
        _volumeLabel = volumeLabel;
        _discSwapper = discSwapper;
        _mountService = mountService;
        _hostStorageProvider = hostStorageProvider;
        _driveEventNotifier = driveEventNotifier;
        _allImagePaths = new List<string>(allImagePaths);
        RebuildOptions(currentImagePath);
    }

    private void RebuildOptions(string currentImagePath) {
        _suppressSelectionHandling = true;
        ComboboxOptions.Clear();
        if (IsHdd) {
            string label = string.IsNullOrEmpty(VolumeLabel) ? $"{DriveLetter}:" : VolumeLabel;
            ComboboxOptions.Add(label);
            SelectedOption = label;
        } else {
            foreach (string path in _allImagePaths) {
                ComboboxOptions.Add(Path.GetFileName(path));
            }
            string currentFileName;
            if (string.IsNullOrEmpty(currentImagePath)) {
                currentFileName = string.Empty;
            } else {
                currentFileName = Path.GetFileName(currentImagePath);
            }
            if (ComboboxOptions.Contains(currentFileName)) {
                SelectedOption = currentFileName;
            } else if (ComboboxOptions.Count > 0) {
                SelectedOption = ComboboxOptions[0];
            } else {
                SelectedOption = string.Empty;
            }
        }
        _suppressSelectionHandling = false;
        OnPropertyChanged(nameof(TooltipText));
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
        if (!string.Equals(VolumeLabel, status.VolumeLabel, StringComparison.Ordinal)) {
            VolumeLabel = status.VolumeLabel;
        }
        RebuildOptions(status.CurrentImagePath);
    }

    partial void OnSelectedOptionChanged(string value) {
        if (_suppressSelectionHandling) {
            return;
        }
        if (IsHdd) {
            return;
        }
        if (string.IsNullOrEmpty(value)) {
            return;
        }
        int index = ComboboxOptions.IndexOf(value);
        if (index >= 0 && index < _allImagePaths.Count) {
            _discSwapper.SwapToImageIndex(DriveLetter, index);
        }
    }

    /// <summary>Opens a folder picker so the user can mount a host folder into this drive.</summary>
    [RelayCommand]
    private async Task MountFolder() {
        FolderPickerOpenOptions options = new() {
            Title = $"Select folder to mount on drive {DriveLetter}:",
            AllowMultiple = false,
        };
        IReadOnlyList<IStorageFolder> folders = await _hostStorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count == 0) {
            return;
        }
        string? path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) {
            return;
        }
        bool success;
        if (DriveType == DosVirtualDriveType.Floppy) {
            success = _mountService.MountFolderAsFloppy(DriveLetter, path);
        } else {
            success = _mountService.MountFolderAsCdRom(DriveLetter, path);
        }
        if (!success) {
            _driveEventNotifier.NotifyError($"Drive {DriveLetter}: mount failed", $"Could not mount folder: {Path.GetFileName(path)}");
        }
    }

    /// <summary>Opens a file picker so the user can mount a disk image into this drive.</summary>
    [RelayCommand]
    private async Task MountImage() {
        FilePickerFileType[] fileTypes = BuildImageFileTypes();
        FilePickerOpenOptions options = new() {
            Title = $"Select image for drive {DriveLetter}:",
            AllowMultiple = false,
            FileTypeFilter = fileTypes,
        };
        IReadOnlyList<IStorageFile> files = await _hostStorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0) {
            return;
        }
        string? path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) {
            return;
        }
        bool success;
        if (DriveType == DosVirtualDriveType.Floppy) {
            success = _mountService.MountImageAsFloppy(DriveLetter, path);
        } else {
            success = _mountService.MountImageAsCdRom(DriveLetter, path);
        }
        if (!success) {
            _driveEventNotifier.NotifyError($"Drive {DriveLetter}: mount failed", $"Could not mount image: {Path.GetFileName(path)}");
        }
    }

    private FilePickerFileType[] BuildImageFileTypes() {
        if (DriveType == DosVirtualDriveType.Floppy) {
            return new[] {
                new FilePickerFileType("Floppy Images") {
                    Patterns = new[] { "*.img", "*.ima", "*.bin", "*.vhd" }
                },
                FilePickerFileTypes.All,
            };
        }
        return new[] {
            new FilePickerFileType("CD-ROM Images") {
                Patterns = new[] { "*.iso", "*.cue", "*.bin" }
            },
            FilePickerFileTypes.All,
        };
    }
}
