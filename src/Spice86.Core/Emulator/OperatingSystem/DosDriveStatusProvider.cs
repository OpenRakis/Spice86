namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

/// <summary>
/// Builds DOS drive-status snapshots that can be shared by the UI and batch command surface.
/// </summary>
public sealed class DosDriveStatusProvider : IDriveStatusProvider {
    private readonly DosDriveManager _dosDriveManager;
    private readonly Mscdex _mscdex;

    /// <summary>
    /// Creates a drive-status provider for the current DOS drive map and MSCDEX state.
    /// </summary>
    /// <param name="dosDriveManager">The DOS drive manager.</param>
    /// <param name="mscdex">The MSCDEX CD-ROM state.</param>
    public DosDriveStatusProvider(DosDriveManager dosDriveManager, Mscdex mscdex) {
        _dosDriveManager = dosDriveManager;
        _mscdex = mscdex;
    }

    /// <inheritdoc />
    public IReadOnlyList<DosVirtualDriveStatus> GetDriveStatuses() {
        List<DosVirtualDriveStatus> statuses = new();

        // Drive letters owned by MSCDEX must not also surface as Fixed entries via the
        // drive map iteration, otherwise callers will observe duplicate status rows.
        HashSet<char> cdRomLetters = new();
        foreach (MscdexDriveEntry cdRom in _mscdex.Drives) {
            cdRomLetters.Add(char.ToUpperInvariant(cdRom.DriveLetter));
        }

        foreach (KeyValuePair<char, DosDriveBase> kvp in _dosDriveManager) {
            if (kvp.Value is not VirtualDrive virtualDrive || kvp.Value is MemoryDrive) {
                continue;
            }
            if (cdRomLetters.Contains(char.ToUpperInvariant(virtualDrive.DriveLetter))) {
                continue;
            }

            DosVirtualDriveType driveType;
            if (virtualDrive.DriveLetter is 'A' or 'B') {
                driveType = DosVirtualDriveType.Floppy;
            } else {
                driveType = DosVirtualDriveType.Fixed;
            }

            if (_dosDriveManager.TryGetFloppyDrive(virtualDrive.DriveLetter, out FloppyDiskDrive? floppyDrive)) {
                if (floppyDrive != null) {
                    statuses.Add(new DosVirtualDriveStatus(
                        floppyDrive.DriveLetter,
                        DosVirtualDriveType.Floppy,
                        hasMedia: true,
                        floppyDrive.Label,
                        currentImagePath: floppyDrive.ImagePath,
                        imageCount: floppyDrive.ImageCount,
                        allImagePaths: floppyDrive.AllImagePaths));
                }
                continue;
            }

            bool hasMedia = !string.IsNullOrEmpty(virtualDrive.MountedHostDirectory);
            if (hasMedia && driveType == DosVirtualDriveType.Floppy) {
                string folderPath = virtualDrive.MountedHostDirectory.TrimEnd('/', '\\');
                string[] folderPaths = [folderPath];
                statuses.Add(new DosVirtualDriveStatus(
                    virtualDrive.DriveLetter,
                    driveType,
                    hasMedia,
                    virtualDrive.Label,
                    currentImagePath: folderPath,
                    imageCount: 1,
                    allImagePaths: folderPaths));
            } else {
                statuses.Add(new DosVirtualDriveStatus(
                    virtualDrive.DriveLetter,
                    driveType,
                    hasMedia,
                    virtualDrive.Label));
            }
        }

        foreach (KeyValuePair<char, MemoryDrive> kvp in _dosDriveManager.MemoryDrives) {
            MemoryDrive memoryDrive = kvp.Value;
            statuses.Add(new DosVirtualDriveStatus(memoryDrive.DriveLetter, DosVirtualDriveType.Memory, hasMedia: true, memoryDrive.Label));
        }

        foreach (MscdexDriveEntry cdRom in _mscdex.Drives) {
            bool hasCdMedia = !cdRom.Drive.MediaState.IsDoorOpen;
            string volumeLabel;
            if (hasCdMedia) {
                volumeLabel = cdRom.Drive.Image.PrimaryVolume.VolumeIdentifier ?? string.Empty;
            } else {
                volumeLabel = string.Empty;
            }
            string imagePath;
            if (hasCdMedia) {
                imagePath = cdRom.Drive.Image.ImagePath;
            } else {
                imagePath = string.Empty;
            }
            statuses.Add(new DosVirtualDriveStatus(
                cdRom.DriveLetter,
                DosVirtualDriveType.CdRom,
                hasCdMedia,
                volumeLabel,
                currentImagePath: imagePath,
                imageCount: cdRom.Drive.ImageCount,
                allImagePaths: cdRom.Drive.AllImagePaths));
        }

        statuses.Sort(static (left, right) => left.DriveLetter.CompareTo(right.DriveLetter));
        return statuses;
    }
}