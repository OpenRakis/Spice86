namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.CdRom.Image;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// Handles MSCDEX INT 2Fh AH=15h subfunctions, dispatching on the AL register.
/// This handler is owned by the <c>Dos</c> class and must not be treated as a standalone service.
/// </summary>
public sealed class MscdexService {
    private const ushort MscdexVersionMajor = 2;
    private const ushort MscdexVersionMinor = 23;

    /// <summary>Magic value written to BX for the drive-check subfunction, per DOSBox Staging (always written, even on miss).</summary>
    private const ushort DriveCheckMagicBx = 0xADAD;

    /// <summary>Magic value written to AX when the drive-check subfunction finds a CD-ROM drive (DOSBox Staging case 0x150B).</summary>
    private const ushort DriveCheckMagicAxValid = 0x5AD8;

    /// <summary>
    /// AX value returned for invalid/unsupported operations, matching DOSBox Staging's
    /// <c>MSCDEX_ERROR_INVALID_FUNCTION = 1</c>.
    /// </summary>
    private const ushort MscdexInvalidFunctionError = 1;

    /// <summary>Number of data bytes per cooked CD-ROM sector.</summary>
    private const int CookedSectorSize = 2048;

    /// <summary>Logical block address of the first volume descriptor on a standard ISO 9660 disc.</summary>
    private const int FirstVolumeDescriptorLba = 16;

    /// <summary>ISO 9660 volume descriptor type byte for the Primary Volume Descriptor.</summary>
    private const byte VolumeDescriptorTypePrimary = 0x01;

    /// <summary>ISO 9660 volume descriptor type byte for the Volume Descriptor Set Terminator.</summary>
    private const byte VolumeDescriptorTypeTerminator = 0xFF;

    /// <summary>
    /// AX error code returned by <see cref="ReadVolumeTableOfContents"/> for a Primary Volume Descriptor,
    /// matching DOSBox Staging's <c>error = (type == 1) ? 1 : …</c>.
    /// </summary>
    private const ushort VolumeDescriptorAxPrimary = 0x0001;

    /// <summary>
    /// AX error code returned by <see cref="ReadVolumeTableOfContents"/> for a VD Set Terminator,
    /// matching DOSBox Staging's <c>… : (type == 0xFF) ? 0xFF : 0</c>.
    /// </summary>
    private const ushort VolumeDescriptorAxTerminator = 0x00FF;

    /// <summary>
    /// DX value returned by <see cref="GetSetVolumeDescriptorPreference"/> for a get request (BX=0),
    /// meaning "prefer Primary Volume Descriptor". Matches DOSBox Staging <c>reg_dx = 0x100</c>.
    /// </summary>
    private const ushort VolumeDescriptorPreferencePrimaryValue = 0x0100;

    /// <summary>Number of bytes written per drive entry in the device-list buffer (subunit byte + 4-byte far pointer).</summary>
    private const uint DeviceListEntrySize = 5;

    // Device driver request header field offsets
    private const uint RequestSubunitOffset = 1;
    private const uint RequestCommandOffset = 2;
    private const uint RequestStatusOffset = 3;
    private const uint RequestDataOffset = 13;

    // IOCTL input buffer pointer is at RequestDataOffset
    private const uint IoctlBufferPtrOffset = RequestDataOffset;

    // Play Audio command data offsets (relative to request base)
    private const uint PlayAudioStartLbaOffset = RequestDataOffset;
    private const uint PlayAudioSectorCountOffset = RequestDataOffset + 4;

    // Device driver command codes
    private const byte CommandIoctlInput = 0x03;
    private const byte CommandPlayAudio = 0x84;
    private const byte CommandStopAudio = 0x85;
    private const byte CommandResumeAudio = 0x88;

    // IOCTL sub-command control codes
    private const byte IoctlDeviceStatus = 0x00;
    private const byte IoctlSectorSize = 0x01;
    private const byte IoctlVolumeSize = 0x02;
    private const byte IoctlMediaChanged = 0x03;
    private const byte IoctlAudioDiskInfo = 0x04;
    private const byte IoctlAudioTrackInfo = 0x05;
    private const byte IoctlAudioSubchannel = 0x06;
    private const byte IoctlAudioStatus = 0x0E;

    // Request status word values
    private const ushort StatusDone = 0x0100;
    private const ushort StatusError = 0x8000;

    // Device status word bits
    private const uint DeviceStatusDoorOpen = 0x0001;
    private const uint DeviceStatusDoorLocked = 0x0002;
    private const uint DeviceStatusAudioPlaying = 0x0200;

    private readonly List<MscdexDriveEntry> _drives = new();
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly ILoggerService _loggerService;

    /// <summary>Gets the registered CD-ROM drives.</summary>
    public IReadOnlyList<MscdexDriveEntry> Drives => _drives;

    /// <summary>
    /// Initialises a new <see cref="MscdexService"/> with no registered drives.
    /// Call <see cref="AddDrive"/> to register CD-ROM drives after construction.
    /// </summary>
    /// <param name="state">The CPU register state.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="loggerService">The logger service.</param>
    public MscdexService(State state, IMemory memory, ILoggerService loggerService) {
        _state = state;
        _memory = memory;
        _loggerService = loggerService;
    }

    /// <summary>
    /// Registers a CD-ROM drive with MSCDEX.
    /// If a drive with the same drive letter is already registered, it is replaced.
    /// </summary>
    /// <param name="drive">The drive entry to register.</param>
    public void AddDrive(MscdexDriveEntry drive) {
        int existingIndex = -1;
        for (int i = 0; i < _drives.Count; i++) {
            if (_drives[i].DriveLetter == drive.DriveLetter) {
                existingIndex = i;
                break;
            }
        }
        if (existingIndex >= 0) {
            _drives[existingIndex] = drive;
        } else {
            _drives.Add(drive);
        }
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("MSCDEX: Registered drive {Drive}: (index {Index})", drive.DriveLetter, drive.DriveIndex);
        }
    }


    /// <summary>
    /// Reads <see cref="State.AL"/> and dispatches to the appropriate MSCDEX subfunction.
    /// Clears the carry flag before dispatch, matching DOSBox Staging's CALLBACK_SCF(false).
    /// </summary>
    public void Dispatch() {
        _state.CarryFlag = false;
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
            case 0x06:
            case 0x07:
                // Debugging on/off — not functional in production MSCDEX; do nothing.
                // Matches DOSBox Staging which silently breaks on these.
                break;
            case 0x08:
                AbsoluteDiskRead();
                break;
            case 0x09:
                AbsoluteDiskWrite();
                break;
            case 0x0A:
                // Reserved — matches DOSBox Staging case 0x150A which does nothing.
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
    /// Sets BX = drive count, CX = first drive index (or 0 if none), and AL = 0xFF,
    /// matching DOSBox Staging's MSCDEX_Handler case 0x1500.
    /// </summary>
    private void GetNumberOfCdRomDrives() {
        _state.BX = (ushort)_drives.Count;
        _state.CX = 0;
        if (_drives.Count > 0) {
            _state.CX = _drives[0].DriveIndex;
        }
        _state.AL = 0xFF;
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
    /// CX = drive index; ES:BX = buffer to receive the filename.
    /// If the drive is not found, sets carry and AX = <see cref="MscdexErrorCode.InvalidDrive"/>,
    /// matching DOSBox Staging's check in case 0x1502-0x1504.
    /// </summary>
    private void GetFileNameInfo() {
        int driveIndex = _state.CX;
        if (!TryGetDrive(driveIndex, out MscdexDriveEntry? _)) {
            _state.CarryFlag = true;
            _state.AX = (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        _memory.UInt8[bufferAddress] = 0;
    }

    /// <summary>
    /// AL=0x05: Reads a volume descriptor sector from the disc and copies it to the caller's buffer.
    /// CX = drive index (0-based, matches DOSBox Staging <c>reg_cx</c>);
    /// DX = descriptor index (0 = PVD, matches DOSBox Staging <c>reg_dx</c>).
    /// On success, sets AX = 1 for PVD (type byte 0x01), AX = 0xFF for VD set terminator
    /// (type byte 0xFF), or AX = 0 for HSFS/unrecognised — matching DOSBox Staging's
    /// <c>error = (type == 1) ? 1 : (type == 0xFF) ? 0xFF : 0; reg_ax = error;</c>.
    /// </summary>
    private void ReadVolumeTableOfContents() {
        int driveIndex = _state.CX;
        if (!TryGetDrive(driveIndex, out MscdexDriveEntry? driveEntry)) {
            _state.CarryFlag = true;
            _state.AX = (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }

        int descriptorIndex = _state.DX;
        int lba = FirstVolumeDescriptorLba + descriptorIndex;
        byte[] sectorBuffer = new byte[CookedSectorSize];
        driveEntry.Drive.Read(lba, sectorCount: 1, sectorBuffer.AsSpan(), CdSectorMode.CookedData2048);

        uint destAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        _memory.LoadData(destAddress, sectorBuffer);

        byte descriptorType = sectorBuffer[0];
        if (descriptorType == VolumeDescriptorTypePrimary) {
            _state.AX = VolumeDescriptorAxPrimary;
        } else if (descriptorType == VolumeDescriptorTypeTerminator) {
            _state.AX = VolumeDescriptorAxTerminator;
        } else {
            _state.AX = 0x0000; // HSFS or unrecognised
        }
        _state.CarryFlag = false;
    }

    /// <summary>
    /// AL=0x08: Reads one or more sectors from the CD-ROM drive into the caller's buffer.
    /// CX = drive index; SI:DI = 32-bit starting LBA (SI is the high word); DX = sector count; ES:BX = destination buffer.
    /// Matches DOSBox Staging's case 0x1508 which uses <c>sector = (reg_si &lt;&lt; 16) + reg_di</c>,
    /// <c>reg_cx</c> for the drive, and <c>reg_dx</c> for the sector count.
    /// </summary>
    private void AbsoluteDiskRead() {
        int driveIndex = _state.CX;
        if (!TryGetDrive(driveIndex, out MscdexDriveEntry? driveEntry)) {
            _state.CarryFlag = true;
            _state.AX = (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }

        uint startLbaUint = ((uint)_state.SI << 16) | _state.DI;
        int startLba = (int)startLbaUint;
        int sectorCount = _state.DX;
        byte[] sectorBuffer = new byte[sectorCount * CookedSectorSize];
        driveEntry.Drive.Read(startLba, sectorCount, sectorBuffer.AsSpan(), CdSectorMode.CookedData2048);

        uint destAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        _memory.LoadData(destAddress, sectorBuffer);
        _state.AX = 0;
        _state.CarryFlag = false;
    }

    /// <summary>
    /// AL=0x09: Absolute disk write — always fails because CD-ROMs are read-only.
    /// Matches DOSBox Staging <c>case 0x1509</c> which returns AX = MSCDEX_ERROR_INVALID_FUNCTION (1)
    /// and sets the carry flag.
    /// </summary>
    private void AbsoluteDiskWrite() {
        _state.AX = MscdexInvalidFunctionError;
        _state.CarryFlag = true;
    }

    /// <summary>
    /// AL=0x0B: Checks whether the drive index in CX corresponds to a known CD-ROM drive.
    /// Writes BX=0xADAD unconditionally; sets AX=0x5AD8 on a match, AX=0x0000 on a miss.
    /// Matches DOSBox Staging's MSCDEX_Handler case 0x150B:
    /// <c>reg_ax = IsValidDrive(reg_cx) ? 0x5AD8 : 0x0000; reg_bx = 0xADAD;</c>
    /// </summary>
    private void CdRomDriveCheck() {
        int driveIndex = _state.CX;
        _state.BX = DriveCheckMagicBx;
        if (TryGetDrive(driveIndex, out MscdexDriveEntry? _)) {
            _state.AX = DriveCheckMagicAxValid;
        } else {
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
    /// Validates the drive via CX. If BX=0 (get), sets DX=0x100 (prefer PVD).
    /// If BX=1 (set), validates DH=1; if DH is not 1, returns invalid-function error.
    /// Any other BX value also returns invalid-function error.
    /// Matches DOSBox Staging's <c>case 0x150E</c>.
    /// </summary>
    private void GetSetVolumeDescriptorPreference() {
        int driveIndex = _state.CX;
        if (!TryGetDrive(driveIndex, out MscdexDriveEntry? _)) {
            _state.AX = (ushort)MscdexErrorCode.InvalidDrive;
            _state.CarryFlag = true;
            return;
        }

        if (_state.BX == 0) {
            // Get preference — return DX=0x100 (prefer primary volume descriptor)
            _state.DX = VolumeDescriptorPreferencePrimaryValue;
        } else if (_state.BX == 1) {
            // Set preference — DH must be 1
            if (_state.DH != 1) {
                _state.AX = MscdexInvalidFunctionError;
                _state.CarryFlag = true;
            }
        } else {
            _state.AX = MscdexInvalidFunctionError;
            _state.CarryFlag = true;
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
    /// AL=0x10: Send Device Driver Request — dispatches the request packet to the appropriate CD-ROM drive.
    /// ES:BX points to the DOS device driver request header.
    /// </summary>
    private void SendDeviceDriverRequest() {
        uint requestBase = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        byte subunit = _memory.UInt8[requestBase + RequestSubunitOffset];
        byte command = _memory.UInt8[requestBase + RequestCommandOffset];

        if (!TryGetDriveBySubUnit(subunit, out MscdexDriveEntry? driveEntry)) {
            _memory.UInt16[requestBase + RequestStatusOffset] = StatusError | (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }

        switch (command) {
            case CommandIoctlInput:
                HandleIoctlInput(requestBase, driveEntry.Drive);
                break;
            case CommandPlayAudio:
                HandlePlayAudio(requestBase, driveEntry.Drive);
                break;
            case CommandStopAudio:
                driveEntry.Drive.StopAudio();
                _memory.UInt16[requestBase + RequestStatusOffset] = StatusDone;
                break;
            case CommandResumeAudio:
                driveEntry.Drive.ResumeAudio();
                _memory.UInt16[requestBase + RequestStatusOffset] = StatusDone;
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("MSCDEX SendDeviceDriverRequest: unknown command 0x{Command:X2}", command);
                }
                _memory.UInt16[requestBase + RequestStatusOffset] = StatusError | (ushort)MscdexErrorCode.InvalidFunction;
                break;
        }
    }

    private void HandlePlayAudio(uint requestBase, ICdRomDrive drive) {
        uint startLba = _memory.UInt32[requestBase + PlayAudioStartLbaOffset];
        uint sectorCount = _memory.UInt32[requestBase + PlayAudioSectorCountOffset];
        drive.PlayAudio((int)startLba, (int)sectorCount);
        _memory.UInt16[requestBase + RequestStatusOffset] = StatusDone;
    }

    private void HandleIoctlInput(uint requestBase, ICdRomDrive drive) {
        ushort bufferOffset = _memory.UInt16[requestBase + IoctlBufferPtrOffset];
        ushort bufferSegment = _memory.UInt16[requestBase + IoctlBufferPtrOffset + 2];
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(bufferSegment, bufferOffset);
        byte controlCode = _memory.UInt8[bufferAddress];

        switch (controlCode) {
            case IoctlDeviceStatus:
                WriteDeviceStatus(bufferAddress, drive);
                break;
            case IoctlSectorSize:
                _memory.UInt8[bufferAddress + 1] = 0;
                _memory.UInt16[bufferAddress + 2] = CookedSectorSize;
                break;
            case IoctlVolumeSize:
                _memory.UInt32[bufferAddress + 1] = (uint)drive.GetDiscInfo().TotalSectors;
                break;
            case IoctlMediaChanged:
                _memory.UInt8[bufferAddress + 1] = drive.MediaState.ReadAndClearMediaChanged() ? (byte)1 : (byte)0;
                break;
            case IoctlAudioDiskInfo:
                WriteAudioDiskInfo(bufferAddress, drive);
                break;
            case IoctlAudioTrackInfo:
                WriteAudioTrackInfo(bufferAddress, drive);
                break;
            case IoctlAudioSubchannel:
                WriteAudioSubchannel(bufferAddress, drive);
                break;
            case IoctlAudioStatus:
                WriteAudioStatus(bufferAddress, drive);
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("MSCDEX IOCTL Input: unknown control code 0x{Code:X2}", controlCode);
                }
                _memory.UInt16[requestBase + RequestStatusOffset] = StatusError | (ushort)MscdexErrorCode.InvalidFunction;
                return;
        }
        _memory.UInt16[requestBase + RequestStatusOffset] = StatusDone;
    }

    private void WriteDeviceStatus(uint bufferAddress, ICdRomDrive drive) {
        uint status = 0;
        if (drive.MediaState.IsDoorOpen) {
            status |= DeviceStatusDoorOpen;
        }
        if (drive.MediaState.IsLocked) {
            status |= DeviceStatusDoorLocked;
        }
        if (drive.IsAudioPlaying) {
            status |= DeviceStatusAudioPlaying;
        }
        _memory.UInt32[bufferAddress + 1] = status;
    }

    private void WriteAudioDiskInfo(uint bufferAddress, ICdRomDrive drive) {
        DiscInfo info = drive.GetDiscInfo();
        _memory.UInt8[bufferAddress + 1] = (byte)info.FirstTrack;
        _memory.UInt8[bufferAddress + 2] = (byte)info.LastTrack;
        _memory.UInt32[bufferAddress + 3] = (uint)info.LeadOutLba;
    }

    private void WriteAudioTrackInfo(uint bufferAddress, ICdRomDrive drive) {
        byte trackNumber = _memory.UInt8[bufferAddress + 1];
        TableOfContentsEntry? entry = drive.GetTrackInfo(trackNumber);
        if (entry == null) {
            _memory.UInt32[bufferAddress + 2] = 0;
            _memory.UInt8[bufferAddress + 6] = 0;
            return;
        }
        _memory.UInt32[bufferAddress + 2] = (uint)entry.Lba;
        _memory.UInt8[bufferAddress + 6] = entry.Control;
    }

    private void WriteAudioSubchannel(uint bufferAddress, ICdRomDrive drive) {
        CdAudioPlayback audioStatus = drive.GetAudioStatus();
        _memory.UInt8[bufferAddress + 1] = 0;
        _memory.UInt8[bufferAddress + 2] = 0;
        _memory.UInt8[bufferAddress + 3] = 0;
        _memory.UInt32[bufferAddress + 4] = 0;
        _memory.UInt32[bufferAddress + 8] = (uint)audioStatus.CurrentLba;
    }

    private void WriteAudioStatus(uint bufferAddress, ICdRomDrive drive) {
        CdAudioPlayback audioStatus = drive.GetAudioStatus();
        bool isPaused = audioStatus.Status == CdAudioStatus.Paused;
        _memory.UInt16[bufferAddress + 1] = isPaused ? (ushort)1 : (ushort)0;
        _memory.UInt32[bufferAddress + 3] = (uint)audioStatus.StartLba;
        _memory.UInt32[bufferAddress + 7] = (uint)audioStatus.EndLba;
    }

    /// <summary>
    /// Finds the drive at position <paramref name="subunit"/> in the <see cref="_drives"/> list.
    /// </summary>
    /// <param name="subunit">Zero-based index into the drives list.</param>
    /// <param name="drive">The matching entry, or <see langword="null"/> when not found.</param>
    /// <returns><see langword="true"/> if a drive at that position exists; otherwise <see langword="false"/>.</returns>
    private bool TryGetDriveBySubUnit(int subunit, [NotNullWhen(true)] out MscdexDriveEntry? drive) {
        if (subunit < 0 || subunit >= _drives.Count) {
            drive = null;
            return false;
        }
        drive = _drives[subunit];
        return true;
    }

    /// <summary>
    /// Logs a warning and returns an error for any unrecognised AL subfunction value.
    /// Matches DOSBox Staging's default case which returns <c>MSCDEX_ERROR_INVALID_FUNCTION = 1</c>.
    /// </summary>
    private void HandleUnknownSubfunction() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("MSCDEX: unknown subfunction AL={AL:X2}", _state.AL);
        }
        _state.CarryFlag = true;
        _state.AX = MscdexInvalidFunctionError;
    }

    /// <summary>
    /// Finds the drive whose <see cref="MscdexDriveEntry.DriveIndex"/> equals <paramref name="driveIndex"/>.
    /// </summary>
    /// <param name="driveIndex">Zero-based drive index to look up.</param>
    /// <param name="drive">The matching entry, or <see langword="null"/> when not found.</param>
    /// <returns><see langword="true"/> if a matching drive was found; otherwise <see langword="false"/>.</returns>
    private bool TryGetDrive(int driveIndex, [NotNullWhen(true)] out MscdexDriveEntry? drive) {
        drive = _drives.FirstOrDefault(e => e.DriveIndex == driveIndex);
        return drive != null;
    }

    /// <summary>
    /// Finds the drive whose <see cref="MscdexDriveEntry.DriveLetter"/> equals <paramref name="letter"/>.
    /// </summary>
    /// <param name="letter">The DOS drive letter to look up (e.g. 'D').</param>
    /// <param name="drive">The matching entry, or <see langword="null"/> when not found.</param>
    /// <returns><see langword="true"/> if a matching drive was found; otherwise <see langword="false"/>.</returns>
    private bool TryGetDriveByLetter(char letter, [NotNullWhen(true)] out MscdexDriveEntry? drive) {
        drive = _drives.FirstOrDefault(e => e.DriveLetter == letter);
        return drive != null;
    }
}
