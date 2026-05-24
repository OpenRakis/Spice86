namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Performs DMA-backed floppy sector transfers and drive-activity notifications.
/// </summary>
public sealed class FloppyDiskTransferService {
    private const string DmaOwnerName = "FloppyDiskTransferService";
    private const int DefaultSectorsPerTrack = 18;
    private const int DefaultNumberOfHeads = 2;

    private readonly IFloppyDriveAccess _floppyAccess;
    private readonly DmaChannel _dmaChannel;
    private readonly IDriveActivityNotifier _activityNotifier;

    /// <summary>
    /// Initialises a new <see cref="FloppyDiskTransferService"/>.
    /// </summary>
    /// <param name="floppyAccess">Low-level floppy drive access for sector reads and writes.</param>
    /// <param name="dmaChannel">DMA channel 2 used for data transfers.</param>
    /// <param name="activityNotifier">Notifier that surfaces per-drive read/write activity.</param>
    public FloppyDiskTransferService(IFloppyDriveAccess floppyAccess, DmaChannel dmaChannel, IDriveActivityNotifier activityNotifier) {
        _floppyAccess = floppyAccess;
        _dmaChannel = dmaChannel;
        _activityNotifier = activityNotifier;
        _dmaChannel.ReserveFor(DmaOwnerName, OnDmaChannelEvicted);
    }

    /// <summary>
    /// Transfers one or more sectors between the floppy image and DMA memory.
    /// </summary>
    /// <param name="driveNumber">Zero-based floppy drive number.</param>
    /// <param name="cylinder">Cylinder to access.</param>
    /// <param name="head">Head to access.</param>
    /// <param name="startSector">First sector number to transfer.</param>
    /// <param name="lastSector">Last sector number to transfer.</param>
    /// <param name="bytesPerSector">Transfer size for each sector.</param>
    /// <param name="isRead"><see langword="true"/> for disk-to-memory reads; <see langword="false"/> for memory-to-disk writes.</param>
    /// <returns><see langword="true"/> when the transfer succeeds; otherwise <see langword="false"/>.</returns>
    public bool TransferSectorsViaDma(byte driveNumber, byte cylinder, byte head, byte startSector, byte lastSector, int bytesPerSector, bool isRead) {
        int sectorsPerTrack = GetSectorsPerTrack(driveNumber);
        int numberOfHeads = GetNumberOfHeads(driveNumber);
        int sectorCount = lastSector - startSector + 1;
        int lba = cylinder * numberOfHeads * sectorsPerTrack + head * sectorsPerTrack + (startSector - 1);
        int byteOffset = lba * bytesPerSector;
        int byteCount = sectorCount * bytesPerSector;
        byte[] buffer = new byte[byteCount];

        if (isRead) {
            bool success = _floppyAccess.ReadFromImage(driveNumber, byteOffset, buffer, 0, byteCount);
            if (!success) {
                return false;
            }

            _dmaChannel.Write(byteCount, buffer);
            _activityNotifier.NotifyRead((char)('A' + driveNumber));
            return true;
        }

        _dmaChannel.Read(byteCount, buffer);
        bool writeSuccess = _floppyAccess.WriteToImage(driveNumber, byteOffset, buffer, 0, byteCount);
        if (!writeSuccess) {
            return false;
        }

        _activityNotifier.NotifyWrite((char)('A' + driveNumber));
        return true;
    }

    private int GetSectorsPerTrack(byte driveNumber) {
        if (_floppyAccess.TryGetGeometry(driveNumber, out int _, out int _, out int sectorsPerTrack, out int _)) {
            return sectorsPerTrack;
        }
        return DefaultSectorsPerTrack;
    }

    private int GetNumberOfHeads(byte driveNumber) {
        if (_floppyAccess.TryGetGeometry(driveNumber, out int _, out int headsPerCylinder, out int _, out int _)) {
            return headsPerCylinder;
        }
        return DefaultNumberOfHeads;
    }

    private void OnDmaChannelEvicted() {
        _dmaChannel.RegisterCallback(null);
    }
}