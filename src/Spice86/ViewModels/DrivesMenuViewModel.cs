namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// View model for the Drives menu, exposing floppy and CD-ROM drives with combobox image selectors.
/// </summary>
public sealed partial class DrivesMenuViewModel : ObservableObject {
    private readonly IDriveStatusProvider _driveStatusProvider;
    private readonly IDiscSwapper _discSwapper;
    private readonly IDriveMountService _mountService;
    private readonly IHostStorageProvider _hostStorageProvider;

    /// <summary>Gets all drive entries (floppy and CD-ROM) available in the menu.</summary>
    public ObservableCollection<DriveMenuItemViewModel> AllDrives { get; } = new();

    /// <summary>
    /// Initialises a new <see cref="DrivesMenuViewModel"/> and starts the polling timer.
    /// </summary>
    /// <param name="driveStatusProvider">The provider that returns the current drive snapshot.</param>
    /// <param name="discSwapper">The disc swapper service.</param>
    /// <param name="mountService">The drive mount service.</param>
    /// <param name="hostStorageProvider">The host storage provider for file picker dialogs.</param>
    public DrivesMenuViewModel(
        IDriveStatusProvider driveStatusProvider,
        IDiscSwapper discSwapper,
        IDriveMountService mountService,
        IHostStorageProvider hostStorageProvider) {
        _driveStatusProvider = driveStatusProvider;
        _discSwapper = discSwapper;
        _mountService = mountService;
        _hostStorageProvider = hostStorageProvider;
        Refresh();
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
}
