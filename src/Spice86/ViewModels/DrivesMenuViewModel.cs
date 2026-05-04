namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

/// <summary>
/// View model for the Drives menu, exposing floppy and CD-ROM drives with combobox image selectors.
/// </summary>
public sealed partial class DrivesMenuViewModel : ObservableObject {
    private readonly IDriveStatusProvider _driveStatusProvider;
    private readonly IDiscSwapper _discSwapper;
    private readonly IDriveMountService _mountService;
    private readonly IHostStorageProvider _hostStorageProvider;
    private IDriveEventNotifier _driveEventNotifier;
    private readonly Dictionary<char, string> _lastKnownImagePath = new();

    /// <summary>Gets all drive entries (floppy and CD-ROM) available in the menu.</summary>
    public ObservableCollection<DriveMenuItemViewModel> AllDrives { get; } = new();

    /// <summary>
    /// Initialises a new <see cref="DrivesMenuViewModel"/> and starts the polling timer.
    /// </summary>
    /// <param name="driveStatusProvider">The provider that returns the current drive snapshot.</param>
    /// <param name="discSwapper">The disc swapper service.</param>
    /// <param name="mountService">The drive mount service.</param>
    /// <param name="hostStorageProvider">The host storage provider for file picker dialogs.</param>
    /// <param name="driveEventNotifier">The notifier used to show toast notifications for drive events.</param>
    public DrivesMenuViewModel(
        IDriveStatusProvider driveStatusProvider,
        IDiscSwapper discSwapper,
        IDriveMountService mountService,
        IHostStorageProvider hostStorageProvider,
        IDriveEventNotifier driveEventNotifier) {
        _driveStatusProvider = driveStatusProvider;
        _discSwapper = discSwapper;
        _mountService = mountService;
        _hostStorageProvider = hostStorageProvider;
        _driveEventNotifier = driveEventNotifier;
        Refresh();
    }

    /// <summary>
    /// Replaces the notification back-end with a real window-based notifier.
    /// Call this from the view code-behind once the <see cref="Avalonia.Controls.Window"/> is loaded.
    /// Clears the tracked state so previously visible drives do not emit spurious toasts.
    /// </summary>
    /// <param name="notifier">The notifier to use from this point on.</param>
    public void AttachNotifier(IDriveEventNotifier notifier) {
        _lastKnownImagePath.Clear();
        _driveEventNotifier = notifier;
    }

    /// <summary>Starts a background timer that polls drive statuses every second.</summary>
    public void StartPolling() {
        DispatcherTimerStarter.StartNewDispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnTimerTick);
    }

    private void OnTimerTick(object? sender, EventArgs e) {
        Refresh();
    }

    /// <summary>Refreshes the <see cref="AllDrives"/> collection from the current drive status snapshot.</summary>
    public void Refresh() {
        IReadOnlyList<DosVirtualDriveStatus> statuses = _driveStatusProvider.GetDriveStatuses();
        List<DosVirtualDriveStatus> relevant = new();
        bool hasCdDrive = false;
        foreach (DosVirtualDriveStatus s in statuses) {
            if (s.DriveType == DosVirtualDriveType.Floppy) {
                relevant.Add(s);
            } else if (s.DriveType == DosVirtualDriveType.CdRom) {
                relevant.Add(s);
                hasCdDrive = true;
            } else if (s.DriveType == DosVirtualDriveType.Fixed) {
                relevant.Add(s);
            }
        }
        if (!hasCdDrive) {
            relevant.Add(new DosVirtualDriveStatus('D', DosVirtualDriveType.CdRom, false, string.Empty));
        }

        int i = 0;
        foreach (DosVirtualDriveStatus status in relevant) {
            NotifyIfChanged(status);
            if (i < AllDrives.Count && AllDrives[i].DriveLetter == status.DriveLetter) {
                AllDrives[i].UpdateFromStatus(status);
            } else {
                while (AllDrives.Count > i && AllDrives[i].DriveLetter != status.DriveLetter) {
                    AllDrives.RemoveAt(i);
                }
                if (i >= AllDrives.Count || AllDrives[i].DriveLetter != status.DriveLetter) {
                    DriveMenuItemViewModel item = new(
                        status.DriveLetter,
                        status.DriveType,
                        status.AllImagePaths,
                        status.CurrentImagePath,
                        status.VolumeLabel,
                        _discSwapper,
                        _mountService,
                        _hostStorageProvider);
                    AllDrives.Insert(i, item);
                }
            }
            i++;
        }
        while (AllDrives.Count > relevant.Count) {
            AllDrives.RemoveAt(AllDrives.Count - 1);
        }
    }

    private void NotifyIfChanged(DosVirtualDriveStatus status) {
        string newPath = status.CurrentImagePath;
        if (!_lastKnownImagePath.TryGetValue(status.DriveLetter, out string? previousPath)) {
            _lastKnownImagePath[status.DriveLetter] = newPath;
            if (!string.IsNullOrEmpty(newPath)) {
                string label = string.IsNullOrEmpty(status.VolumeLabel) ? $"{status.DriveLetter}:" : status.VolumeLabel;
                _driveEventNotifier.Notify($"Drive {status.DriveLetter}: mounted", $"{label} \u2014 {Path.GetFileName(newPath)}");
            }
            return;
        }
        if (!string.Equals(previousPath, newPath, StringComparison.Ordinal)) {
            _lastKnownImagePath[status.DriveLetter] = newPath;
            if (string.IsNullOrEmpty(newPath)) {
                _driveEventNotifier.Notify($"Drive {status.DriveLetter}: ejected", $"No media in drive {status.DriveLetter}:");
            } else if (string.IsNullOrEmpty(previousPath)) {
                string label = string.IsNullOrEmpty(status.VolumeLabel) ? $"{status.DriveLetter}:" : status.VolumeLabel;
                _driveEventNotifier.Notify($"Drive {status.DriveLetter}: mounted", $"{label} \u2014 {Path.GetFileName(newPath)}");
            } else {
                string label = string.IsNullOrEmpty(status.VolumeLabel) ? $"{status.DriveLetter}:" : status.VolumeLabel;
                _driveEventNotifier.Notify($"Drive {status.DriveLetter}: disc swapped", $"{label} \u2014 {Path.GetFileName(newPath)}");
            }
        }
    }
}
