namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.CdRom.Image;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Handles MSCDEX INT 2Fh AH=15h subfunctions, dispatching on the AL register.
/// </summary>
public sealed class MscdexService {
    private const ushort MscdexVersionMajor = 2;
    private const ushort MscdexVersionMinor = 23;

    /// <summary>Magic value written to CX when a drive is recognised by the drive-check subfunction.</summary>
    private const ushort DriveCheckMagicCx = 0xADAD;

    /// <summary>Magic value written to AX when a drive is recognised by the drive-check subfunction ('CR' in ASCII).</summary>
    private const ushort DriveCheckMagicAx = 0x5243;

    /// <summary>Number of data bytes per cooked CD-ROM sector.</summary>
    private const int CookedSectorSize = 2048;

    /// <summary>Logical block address of the first volume descriptor on a standard ISO 9660 disc.</summary>
    private const int FirstVolumeDescriptorLba = 16;

    /// <summary>Number of bytes written per drive entry in the device-list buffer (subunit byte + 4-byte far pointer).</summary>
    private const uint DeviceListEntrySize = 5;

    private readonly IReadOnlyList<MscdexDriveEntry> _drives;
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly ILoggerService _loggerService;

    /// <summary>
    /// Initialises a new <see cref="MscdexService"/>.
    /// </summary>
    /// <param name="drives">Ordered list of registered CD-ROM drives.</param>
    /// <param name="state">The CPU register state.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="loggerService">The logger service.</param>
    public MscdexService(IReadOnlyList<MscdexDriveEntry> drives, State state, IMemory memory, ILoggerService loggerService) {
        _drives = drives;
        _state = state;
        _memory = memory;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Reads <see cref="State.AL"/> and dispatches to the appropriate MSCDEX subfunction.
    /// </summary>
    public void Dispatch() {
        switch (_state.AL) {
            case 0x00:
                GetNumberOfCdRomDrives();
                break;
            case 0x01:
                GetCdRomDriveDeviceList();
                break;
            case 0x02:
            case 0x03:
            case 0x04:
                GetFileNameInfo();
                break;
            case 0x05:
                ReadVolumeTableOfContents();
                break;
            case 0x08:
                AbsoluteDiskRead();
                break;
            case 0x09:
                AbsoluteDiskWrite();
                break;
            case 0x0B:
                CdRomDriveCheck();
                break;
            case 0x0C:
                GetMscdexVersion();
                break;
            case 0x0D:
                GetCdRomDriveLetters();
                break;
            case 0x0E:
                GetSetVolumeDescriptorPreference();
                break;
            case 0x0F:
                GetDirectoryEntry();
                break;
            case 0x10:
                SendDeviceDriverRequest();
                break;
            default:
                HandleUnknownSubfunction();
                break;
        }
    }

    /// <summary>
    /// AL=0x00: Returns the number of registered CD-ROM drives and the index of the first one.
    /// </summary>
    private void GetNumberOfCdRomDrives() {
        _state.BX = (ushort)_drives.Count;
        if (_drives.Count > 0) {
            _state.CX = _drives[0].DriveIndex;
        } else {
            _state.CX = 0;
        }
    }

    /// <summary>
    /// AL=0x01: Fills the caller's buffer at ES:BX with a 5-byte entry per drive
    /// (subunit byte + 4-byte placeholder far pointer to the device driver header).
    /// </summary>
    private void GetCdRomDriveDeviceList() {
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        for (int i = 0; i < _drives.Count; i++) {
            uint entryAddress = bufferAddress + (uint)i * DeviceListEntrySize;
            _memory.UInt8[entryAddress] = 0;         // subunit index
            _memory.UInt8[entryAddress + 1] = 0;     // far ptr offset low
            _memory.UInt8[entryAddress + 2] = 0;     // far ptr offset high
            _memory.UInt8[entryAddress + 3] = 0;     // far ptr segment low
            _memory.UInt8[entryAddress + 4] = 0;     // far ptr segment high
        }
    }

    /// <summary>
    /// AL=0x02/0x03/0x04: Returns an empty (null-terminated) string for copyright, abstract,
    /// and bibliographic file names.
    /// </summary>
    private void GetFileNameInfo() {
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        _memory.UInt8[bufferAddress] = 0;
    }

    /// <summary>
    /// AL=0x05: Reads a volume descriptor sector from the disc and copies it to the caller's buffer.
    /// BP selects the drive (0-based); CX selects which descriptor to read (0 = PVD).
    /// Returns the descriptor type byte in AL.
    /// </summary>
    private void ReadVolumeTableOfContents() {
        int driveIndex = _state.BP;
        if (!TryGetDrive(driveIndex, out MscdexDriveEntry? driveEntry)) {
            _state.CarryFlag = true;
            _state.AX = (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }

        int descriptorIndex = _state.CX;
        int lba = FirstVolumeDescriptorLba + descriptorIndex;
        byte[] sectorBuffer = new byte[CookedSectorSize];
        driveEntry.Drive.Read(lba, sectorCount: 1, sectorBuffer.AsSpan(), CdSectorMode.CookedData2048);

        uint destAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        _memory.LoadData(destAddress, sectorBuffer);

        _state.AL = sectorBuffer[0];
        _state.CarryFlag = false;
    }

    /// <summary>
    /// AL=0x08: Reads one or more raw sectors from the first CD-ROM drive into the caller's buffer.
    /// CX = sector count; DX = starting LBA; ES:BX = destination buffer.
    /// </summary>
    private void AbsoluteDiskRead() {
        if (_drives.Count == 0) {
            _state.CarryFlag = true;
            _state.AX = (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }

        MscdexDriveEntry driveEntry = _drives[0];
        int sectorCount = _state.CX;
        int startLba = _state.DX;
        byte[] sectorBuffer = new byte[sectorCount * CookedSectorSize];
        driveEntry.Drive.Read(startLba, sectorCount, sectorBuffer.AsSpan(), CdSectorMode.CookedData2048);

        uint destAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        _memory.LoadData(destAddress, sectorBuffer);
        _state.CarryFlag = false;
    }

    /// <summary>
    /// AL=0x09: Absolute disk write — always fails because CD-ROMs are read-only.
    /// </summary>
    private void AbsoluteDiskWrite() {
        _state.AX = (ushort)MscdexErrorCode.WriteProtect;
        _state.CarryFlag = true;
    }

    /// <summary>
    /// AL=0x0B: Checks whether the drive index in BX corresponds to a known CD-ROM drive.
    /// Sets CX=0xADAD and AX=0x5243 on match; zeros both on miss.
    /// </summary>
    private void CdRomDriveCheck() {
        int driveIndex = _state.BX;
        if (TryGetDrive(driveIndex, out MscdexDriveEntry? _)) {
            _state.CX = DriveCheckMagicCx;
            _state.AX = DriveCheckMagicAx;
        } else {
            _state.CX = 0;
            _state.AX = 0;
        }
    }

    /// <summary>
    /// AL=0x0C: Returns the MSCDEX version number.
    /// BH = major version (2), BL = minor version (23 decimal = 0x17 hex), so BX = 0x0217.
    /// </summary>
    private void GetMscdexVersion() {
        _state.BX = (ushort)((MscdexVersionMajor << 8) | MscdexVersionMinor);
    }

    /// <summary>
    /// AL=0x0D: Writes one byte per registered drive into the caller's buffer at ES:BX.
    /// Each byte is the zero-based drive letter index (A=0, B=1, …).
    /// </summary>
    private void GetCdRomDriveLetters() {
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        for (int i = 0; i < _drives.Count; i++) {
            _memory.UInt8[bufferAddress + (uint)i] = _drives[i].DriveIndex;
        }
    }

    /// <summary>
    /// AL=0x0E: Get/Set Volume Descriptor Preference.
    /// If BX=0 (get), returns BX=1 (prefer primary volume descriptor) and DX=0.
    /// If BX=1 (set), accepts the preference silently.
    /// </summary>
    private void GetSetVolumeDescriptorPreference() {
        if (_state.BX == 0) {
            _state.BX = 1;
            _state.DX = 0;
        }
    }

    /// <summary>
    /// AL=0x0F: Get Directory Entry — not implemented; sets carry and returns an error.
    /// </summary>
    private void GetDirectoryEntry() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("{MethodName}: not implemented", nameof(GetDirectoryEntry));
        }
        _state.CarryFlag = true;
        _state.AX = (ushort)MscdexErrorCode.InvalidFunction;
    }

    /// <summary>
    /// AL=0x10: Send Device Driver Request — returns success without forwarding.
    /// </summary>
    private void SendDeviceDriverRequest() {
        _state.CarryFlag = false;
    }

    /// <summary>
    /// Logs a warning and returns an error for any unrecognised AL subfunction value.
    /// </summary>
    private void HandleUnknownSubfunction() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("MSCDEX: unknown subfunction AL={AL:X2}", _state.AL);
        }
        _state.CarryFlag = true;
        _state.AX = (ushort)MscdexErrorCode.InvalidFunction;
    }

    /// <summary>
    /// Finds the drive whose <see cref="MscdexDriveEntry.DriveIndex"/> equals <paramref name="driveIndex"/>.
    /// </summary>
    /// <param name="driveIndex">Zero-based drive index to look up.</param>
    /// <param name="drive">The matching entry, or <see langword="null"/> when not found.</param>
    /// <returns><see langword="true"/> if a matching drive was found; otherwise <see langword="false"/>.</returns>
    private bool TryGetDrive(int driveIndex, [NotNullWhen(true)] out MscdexDriveEntry? drive) {
        foreach (MscdexDriveEntry entry in _drives) {
            if (entry.DriveIndex == driveIndex) {
                drive = entry;
                return true;
            }
        }
        drive = null;
        return false;
    }

    /// <summary>
    /// Finds the drive whose <see cref="MscdexDriveEntry.DriveLetter"/> equals <paramref name="letter"/>.
    /// </summary>
    /// <param name="letter">The DOS drive letter to look up (e.g. 'D').</param>
    /// <param name="drive">The matching entry, or <see langword="null"/> when not found.</param>
    /// <returns><see langword="true"/> if a matching drive was found; otherwise <see langword="false"/>.</returns>
    private bool TryGetDriveByLetter(char letter, [NotNullWhen(true)] out MscdexDriveEntry? drive) {
        foreach (MscdexDriveEntry entry in _drives) {
            if (entry.DriveLetter == letter) {
                drive = entry;
                return true;
            }
        }
        drive = null;
        return false;
    }
}
