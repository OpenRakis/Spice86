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
/// Polls <see cref="IDriveStatusProvider"/> on a regular interval and exposes
/// the current drive statuses as an observable collection for binding in the UI.
/// </summary>
public sealed partial class DriveStatusViewModel : ViewModelBase {
    private readonly IDriveStatusProvider _driveStatusProvider;

    /// <summary>
    /// Gets the observable collection of drive status entries, updated by the polling timer.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DosVirtualDriveStatus> _driveStatuses = new();

    /// <summary>
    /// Initialises a new <see cref="DriveStatusViewModel"/> and starts the polling timer.
    /// </summary>
    /// <param name="driveStatusProvider">The provider that returns the current drive snapshot.</param>
    public DriveStatusViewModel(IDriveStatusProvider driveStatusProvider) {
        _driveStatusProvider = driveStatusProvider;
        DispatcherTimerStarter.StartNewDispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnTimerTick);
        RefreshDriveStatuses();
    }

    private void OnTimerTick(object? sender, EventArgs e) {
        RefreshDriveStatuses();
    }

    private void RefreshDriveStatuses() {
        IReadOnlyList<DosVirtualDriveStatus> current = _driveStatusProvider.GetDriveStatuses();

        // Rebuild the collection only when the content changes to avoid unnecessary layout passes.
        if (HasChanged(current)) {
            DriveStatuses.Clear();
            foreach (DosVirtualDriveStatus status in current) {
                DriveStatuses.Add(status);
            }
            OnPropertyChanged(nameof(SummaryColor));
            OnPropertyChanged(nameof(DriveCountText));
        }
    }

    private bool HasChanged(IReadOnlyList<DosVirtualDriveStatus> incoming) {
        if (incoming.Count != DriveStatuses.Count) {
            return true;
        }
        for (int i = 0; i < incoming.Count; i++) {
            DosVirtualDriveStatus a = incoming[i];
            DosVirtualDriveStatus b = DriveStatuses[i];
            if (a.DriveLetter != b.DriveLetter ||
                a.DriveType != b.DriveType ||
                a.HasMedia != b.HasMedia ||
                !string.Equals(a.VolumeLabel, b.VolumeLabel, StringComparison.Ordinal) ||
                !string.Equals(a.CurrentImagePath, b.CurrentImagePath, StringComparison.Ordinal) ||
                a.ImageCount != b.ImageCount) {
                return true;
            }
        }
        return false;
    }

    /// <summary>Gets the indicator color: green if any drive has media, grey otherwise.</summary>
    public string SummaryColor {
        get {
            foreach (DosVirtualDriveStatus status in DriveStatuses) {
                if (status.HasMedia) {
                    return "#2ecc71";
                }
            }
            return "#888888";
        }
    }

    /// <summary>Gets a short text showing the number of drives.</summary>
    public string DriveCountText => $"Drives ({DriveStatuses.Count})";
}
