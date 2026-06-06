namespace Spice86.Core.Emulator.InterruptHandlers.Mscdex;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.CdRom.Subchannel;
using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Handles MSCDEX INT 2Fh AH=15h subfunctions, dispatching on the AL register.
/// </summary>
public sealed class Mscdex {
    private const ushort MscdexVersionMajor = 2;
    private const ushort MscdexVersionMinor = 23;

    /// <summary>Magic value written to BX for the drive-check subfunction (always written, even on miss).</summary>
    private const ushort DriveCheckMagicBx = 0xADAD;

    /// <summary>Magic value written to AX when the drive-check subfunction finds a CD-ROM drive.</summary>
    private const ushort DriveCheckMagicAxValid = 0x5AD8;

    /// <summary>
    /// AX value returned for invalid/unsupported operations
    /// </summary>
    private const ushort MscdexInvalidFunctionError = 1;

    /// <summary>Number of data bytes per cooked CD-ROM sector.</summary>
    private const int CookedSectorSize = 2048;

    /// <summary>Number of bytes per raw CD-ROM sector (sync + header + data + error correction).</summary>
    private const int RawSectorSize = 2352;

    /// <summary>Logical block address of the first volume descriptor on a standard ISO 9660 disc.</summary>
    private const int FirstVolumeDescriptorLba = 16;

    /// <summary>ISO 9660 volume descriptor type byte for the Primary Volume Descriptor.</summary>
    private const byte VolumeDescriptorTypePrimary = 0x01;

    /// <summary>ISO 9660 volume descriptor type byte for the Volume Descriptor Set Terminator.</summary>
    private const byte VolumeDescriptorTypeTerminator = 0xFF;

    /// <summary>
    /// AX error code returned by <see cref="ReadVolumeTableOfContents"/> for a Primary Volume Descriptor,
    /// </summary>
    private const ushort VolumeDescriptorAxPrimary = 0x0001;

    /// <summary>
    /// AX error code returned by <see cref="ReadVolumeTableOfContents"/> for a VD Set Terminator,
    /// </summary>
    private const ushort VolumeDescriptorAxTerminator = 0x00FF;

    /// <summary>
    /// DX value returned by <see cref="GetSetVolumeDescriptorPreference"/> for a get request (BX=0),
    /// meaning "prefer Primary Volume Descriptor".</c>.
    /// </summary>
    private const ushort VolumeDescriptorPreferencePrimaryValue = 0x0100;

    /// <summary>Number of bytes written per drive entry in the device-list buffer (subunit byte + 4-byte far pointer).</summary>
    private const uint DeviceListEntrySize = 5;

    // Device driver request layout and command codes are shared with tests.

    // Channel control: 4 channels, each with output-map and volume byte
    private const int ChannelCount = 4;

    // Request status word values
    private const ushort StatusDone = 0x0100;
    private const ushort StatusError = 0x8000;
    private const ushort StatusAudioPlaying = 0x0200;

    // Device status word bits
    private const uint DeviceStatusDoorOpen = 1u << 0;
    private const uint DeviceStatusDoorLocked = 1u << 1;
    private const uint DeviceStatusSupportsRawCooked = 1u << 2;
    private const uint DeviceStatusCanReadAudio = 1u << 4;
    private const uint DeviceStatusCanControlAudio = 1u << 8;
    private const uint DeviceStatusSupportsRedbookHsg = 1u << 9;
    private const uint DeviceStatusAudioPlaying = 1u << 10;
    private const uint DeviceStatusDriveEmpty = 1u << 11;

    /// <summary>
    /// Red Book pre-gap offset in frames (2 seconds × 75 frames/second).
    /// LBA 0 on a CD corresponds to absolute frame 150; adding this when converting
    /// LBA → MSF gives the correct Red Book position for IOCTL audio responses,
    /// </summary>
    private const int RedbookPreGapFrames = 150;

    private readonly List<MscdexDriveEntry> _drives = new();
    private readonly State _state;
    private readonly IMemory _memory;
    private readonly ILoggerService _loggerService;
    private readonly IDriveActivityNotifier? _activityNotifier;
    private readonly Dictionary<char, MscdexAudioState> _audioStates = new();

    // Audio channel output mapping: channel i routes to channel _channelOutputMap[i]
    // Initial identity mapping: each channel maps to itself at full volume (255)
    private readonly byte[] _channelOutputMap = { 0, 1, 2, 3 };
    private readonly byte[] _channelVolumes = { 255, 255, 255, 255 };

    /// <summary>Gets the registered CD-ROM drives.</summary>
    public IReadOnlyList<MscdexDriveEntry> Drives => _drives;

    /// <summary>
    /// Initialises a new <see cref="Mscdex"/> with no registered drives.
    /// Call <see cref="AddDrive"/> to register CD-ROM drives after construction.
    /// </summary>
    /// <param name="state">The CPU register state.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="loggerService">The logger service.</param>
    public Mscdex(State state, IMemory memory, ILoggerService loggerService)
        : this(state, memory, loggerService, null) {
    }

    /// <summary>
    /// Initialises a new <see cref="Mscdex"/> with no registered drives and an activity notifier.
    /// Call <see cref="AddDrive"/> to register CD-ROM drives after construction.
    /// </summary>
    /// <param name="state">The CPU register state.</param>
    /// <param name="memory">The memory bus.</param>
    /// <param name="loggerService">The logger service.</param>
    /// <param name="activityNotifier">Notifier that surfaces per-drive read activity to the UI (may be null).</param>
    public Mscdex(State state, IMemory memory, ILoggerService loggerService,
        IDriveActivityNotifier? activityNotifier) {
        _state = state;
        _memory = memory;
        _loggerService = loggerService;
        _activityNotifier = activityNotifier;
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
        _audioStates[drive.DriveLetter] = new MscdexAudioState();
        if (_loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("MSCDEX: Registered drive {Drive}: (index {Index})", drive.DriveLetter, drive.DriveIndex);
        }
    }

    /// <summary>
    /// Removes a previously registered CD-ROM drive.
    /// </summary>
    /// <param name="driveLetter">The DOS drive letter to remove.</param>
    /// <returns><see langword="true"/> when a drive was removed; otherwise <see langword="false"/>.</returns>
    public bool RemoveDrive(char driveLetter) {
        char upper = char.ToUpperInvariant(driveLetter);
        for (int i = 0; i < _drives.Count; i++) {
            if (_drives[i].DriveLetter != upper) {
                continue;
            }

            _drives.RemoveAt(i);
            _audioStates.Remove(upper);
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("MSCDEX: Removed drive {Drive}:", upper);
            }
            return true;
        }

        return false;
    }


    /// <summary>
    /// Reads <see cref="State.AL"/> and dispatches to the appropriate MSCDEX subfunction.
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
                // Debugging on/off — not functional; do nothing.
                break;
            case 0x08:
                AbsoluteDiskRead();
                break;
            case 0x09:
                AbsoluteDiskWrite();
                break;
            case 0x0A:
                // Reserved; which does nothing.
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
    /// Sets BX = drive count, CX = first drive index (or 0 if none), and AL = 0xFF.
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
    /// If the drive is not found, sets carry and AX = <see cref="MscdexErrorCode.InvalidDrive"/>.
    /// </summary>
    private void GetFileNameInfo() {
        int driveIndex = _state.CX;
        MscdexDriveLookup driveLookup = GetDriveByIndex(driveIndex);
        if (!driveLookup.IsPresent) {
            _state.CarryFlag = true;
            _state.AX = (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        _memory.UInt8[bufferAddress] = 0;
    }

    /// <summary>
    /// AL=0x05: Reads a volume descriptor sector from the disc and copies it to the caller's buffer.
    /// CX = drive index
    /// DX = descriptor index (0 = PVD).
    /// On success, sets AX = 1 for PVD (type byte 0x01), AX = 0xFF for VD set terminator
    /// (type byte 0xFF), or AX = 0 for HSFS/unrecognised —
    /// <c>error = (type == 1) ? 1 : (type == 0xFF) ? 0xFF : 0; reg_ax = error;</c>.
    /// </summary>
    private void ReadVolumeTableOfContents() {
        int driveIndex = _state.CX;
        MscdexDriveLookup driveLookup = GetDriveByIndex(driveIndex);
        if (!driveLookup.IsPresent) {
            _state.CarryFlag = true;
            _state.AX = (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }
        MscdexDriveEntry driveEntry = _drives[driveLookup.DriveListIndex];

        int descriptorIndex = _state.DX;
        int lba = FirstVolumeDescriptorLba + descriptorIndex;
        byte[] sectorBuffer = new byte[CookedSectorSize];
        driveEntry.Drive.Read(lba, sectorCount: 1, sectorBuffer.AsSpan(), CdSectorMode.CookedData2048);
        _activityNotifier?.NotifyRead(driveEntry.DriveLetter);

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
    /// </summary>
    private void AbsoluteDiskRead() {
        int driveIndex = _state.CX;
        MscdexDriveLookup driveLookup = GetDriveByIndex(driveIndex);
        if (!driveLookup.IsPresent) {
            _state.CarryFlag = true;
            _state.AX = (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }
        MscdexDriveEntry driveEntry = _drives[driveLookup.DriveListIndex];

        uint startLbaUint = ((uint)_state.SI << 16) | _state.DI;
        int startLba = (int)startLbaUint;
        int sectorCount = _state.DX;
        byte[] sectorBuffer = new byte[sectorCount * CookedSectorSize];
        driveEntry.Drive.Read(startLba, sectorCount, sectorBuffer.AsSpan(), CdSectorMode.CookedData2048);
        _activityNotifier?.NotifyRead(driveEntry.DriveLetter);

        uint destAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        _memory.LoadData(destAddress, sectorBuffer);
        _state.AX = 0;
        _state.CarryFlag = false;
    }

    /// <summary>
    /// AL=0x09: Absolute disk write — always fails because CD-ROMs are read-only.
    /// </summary>
    private void AbsoluteDiskWrite() {
        _state.AX = MscdexInvalidFunctionError;
        _state.CarryFlag = true;
    }

    /// <summary>
    /// AL=0x0B: Checks whether the drive index in CX corresponds to a known CD-ROM drive.
    /// Writes BX=0xADAD unconditionally; sets AX=0x5AD8 on a match, AX=0x0000 on a miss.
    /// </summary>
    private void CdRomDriveCheck() {
        int driveIndex = _state.CX;
        _state.BX = DriveCheckMagicBx;
        if (GetDriveByIndex(driveIndex).IsPresent) {
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
    /// </summary>
    private void GetSetVolumeDescriptorPreference() {
        int driveIndex = _state.CX;
        MscdexDriveLookup driveLookup = GetDriveByIndex(driveIndex);
        if (!driveLookup.IsPresent) {
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
        byte subunit = _memory.UInt8[requestBase + MscdexRequestOffsets.RequestSubunitOffset];
        byte command = _memory.UInt8[requestBase + MscdexRequestOffsets.RequestCommandOffset];

        MscdexDriveLookup driveLookup = GetDriveBySubUnit(subunit);
        if (!driveLookup.IsPresent) {
            _memory.UInt16[requestBase + MscdexRequestOffsets.RequestStatusOffset] = StatusError | (ushort)MscdexErrorCode.InvalidDrive;
            return;
        }
        MscdexDriveEntry driveEntry = _drives[driveLookup.DriveListIndex];

        switch ((MscdexDeviceDriverCommand)command) {
            case MscdexDeviceDriverCommand.IoctlInput:
                HandleIoctlInput(requestBase, driveEntry);
                break;
            case MscdexDeviceDriverCommand.IoctlOutput:
                HandleIoctlOutput(requestBase, driveEntry);
                break;
            case MscdexDeviceDriverCommand.DeviceOpen:
            case MscdexDeviceDriverCommand.DeviceClose:
                // Device open/close — no-op
                WriteRequestStatus(requestBase, driveEntry, StatusDone);
                break;
            case MscdexDeviceDriverCommand.ReadLong:
            case MscdexDeviceDriverCommand.ReadLongPrefetch:
                HandleReadLong(requestBase, driveEntry);
                break;
            case MscdexDeviceDriverCommand.Seek:
                HandleSeek(requestBase, driveEntry);
                break;
            case MscdexDeviceDriverCommand.PlayAudio:
                HandlePlayAudio(requestBase, driveEntry);
                break;
            case MscdexDeviceDriverCommand.StopAudio:
                StopAudio(driveEntry);
                WriteRequestStatus(requestBase, driveEntry, StatusDone);
                break;
            case MscdexDeviceDriverCommand.ResumeAudio:
                ResumeAudio(driveEntry);
                WriteRequestStatus(requestBase, driveEntry, StatusDone);
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("MSCDEX SendDeviceDriverRequest: unknown command 0x{Command:X2}", command);
                }
                WriteRequestStatus(requestBase, driveEntry, StatusError | (ushort)MscdexErrorCode.InvalidFunction);
                break;
        }
    }

    private void HandlePlayAudio(uint requestBase, MscdexDriveEntry driveEntry) {
        ICdRomDrive drive = driveEntry.Drive;
        byte addressingMode = _memory.UInt8[requestBase + MscdexRequestOffsets.RequestAddressingModeOffset];
        uint startRaw = _memory.UInt32[requestBase + MscdexRequestOffsets.PlayAudioStartLbaOffset];
        uint sectorCount = _memory.UInt32[requestBase + MscdexRequestOffsets.PlayAudioSectorCountOffset];
        int startLba = TranslateRequestSector(addressingMode, startRaw);
        drive.PlayAudio(startLba, (int)sectorCount);
        MscdexAudioState audioState = GetAudioState(driveEntry);
        audioState.StartLba = startLba;
        audioState.Length = (int)sectorCount;
        WriteRequestStatus(requestBase, driveEntry, StatusDone);
    }

    private void HandleSeek(uint requestBase, MscdexDriveEntry driveEntry) {
        byte addressingMode = _memory.UInt8[requestBase + MscdexRequestOffsets.RequestAddressingModeOffset];
        uint startRaw = _memory.UInt32[requestBase + MscdexRequestOffsets.ReadLongStartSectorOffset];
        int startLba = TranslateRequestSector(addressingMode, startRaw);
        driveEntry.Drive.StopAudio();
        MscdexAudioState audioState = GetAudioState(driveEntry);
        audioState.StartLba = startLba;
        audioState.Length = 0;
        WriteRequestStatus(requestBase, driveEntry, StatusDone);
    }

    private void HandleIoctlInput(uint requestBase, MscdexDriveEntry driveEntry) {
        ICdRomDrive drive = driveEntry.Drive;
        ushort bufferOffset = _memory.UInt16[requestBase + MscdexRequestOffsets.IoctlBufferPtrOffset];
        ushort bufferSegment = _memory.UInt16[requestBase + MscdexRequestOffsets.IoctlBufferPtrOffset + 2];
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(bufferSegment, bufferOffset);
        byte controlCode = _memory.UInt8[bufferAddress];

        switch ((MscdexIoctlInputCode)controlCode) {
            case MscdexIoctlInputCode.DeviceHeaderAddress:
                // 0x00: Return Device Header address — write a zero far pointer (no real driver header installed)
                _memory.UInt32[bufferAddress + 1] = 0;
                break;
            case MscdexIoctlInputCode.CurrentPosition:
                if (!WriteCurrentPosition(bufferAddress, driveEntry)) {
                    WriteRequestStatus(requestBase, driveEntry, StatusError | (ushort)MscdexErrorCode.BadCommand);
                    return;
                }
                break;
            case MscdexIoctlInputCode.ChannelControl:
                WriteChannelControl(bufferAddress);
                break;
            case MscdexIoctlInputCode.DeviceStatus:
                WriteDeviceStatus(bufferAddress, drive);
                break;
            case MscdexIoctlInputCode.SectorSize:
                // 0x07: mode byte at buffer+1 (0=cooked 2048, 1=raw 2352); write sector size word at buffer+2.
                byte sectorSizeMode = _memory.UInt8[bufferAddress + 1];
                if (sectorSizeMode == 0) {
                    _memory.UInt16[bufferAddress + 2] = CookedSectorSize;
                } else if (sectorSizeMode == 1) {
                    _memory.UInt16[bufferAddress + 2] = RawSectorSize;
                } else {
                    WriteRequestStatus(requestBase, driveEntry, StatusError | (ushort)MscdexErrorCode.BadCommand);
                    return;
                }
                break;
            case MscdexIoctlInputCode.VolumeSize:
                _memory.UInt32[bufferAddress + 1] = (uint)drive.GetDiscInfo().TotalSectors;
                break;
            case MscdexIoctlInputCode.MediaChanged:
                _memory.UInt8[bufferAddress + 1] = drive.MediaState.ReadAndClearMediaChanged() ? (byte)0xFF : (byte)0x01;
                break;
            case MscdexIoctlInputCode.AudioDiskInfo:
                WriteAudioDiskInfo(bufferAddress, drive);
                break;
            case MscdexIoctlInputCode.AudioTrackInfo:
                WriteAudioTrackInfo(bufferAddress, drive);
                break;
            case MscdexIoctlInputCode.AudioSubchannel:
                WriteAudioSubchannel(bufferAddress, drive);
                break;
            case MscdexIoctlInputCode.UpcCode:
                WriteUpcCode(bufferAddress, drive);
                break;
            case MscdexIoctlInputCode.AudioStatus:
                WriteAudioStatus(bufferAddress, driveEntry);
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("MSCDEX IOCTL Input: unknown control code 0x{Code:X2}", controlCode);
                }
                WriteRequestStatus(requestBase, driveEntry, StatusError | (ushort)MscdexErrorCode.InvalidFunction);
                return;
        }
        WriteRequestStatus(requestBase, driveEntry, StatusDone);
    }

    private void HandleIoctlOutput(uint requestBase, MscdexDriveEntry driveEntry) {
        ICdRomDrive drive = driveEntry.Drive;
        ushort bufferOffset = _memory.UInt16[requestBase + MscdexRequestOffsets.IoctlBufferPtrOffset];
        ushort bufferSegment = _memory.UInt16[requestBase + MscdexRequestOffsets.IoctlBufferPtrOffset + 2];
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(bufferSegment, bufferOffset);
        byte controlCode = _memory.UInt8[bufferAddress];

        switch ((MscdexIoctlOutputCode)controlCode) {
            case MscdexIoctlOutputCode.Eject:
                drive.Eject();
                break;
            case MscdexIoctlOutputCode.LockDoor:
                // Lock/unlock door — no-op
                break;
            case MscdexIoctlOutputCode.ResetDrive:
                // Reset drive: stop audio playback
                StopAudio(driveEntry);
                break;
            case MscdexIoctlOutputCode.ChannelControl:
                // Read 4-channel volume/mapping from the IOCTL buffer and store it
                for (int chan = 0; chan < ChannelCount; chan++) {
                    _channelOutputMap[chan] = _memory.UInt8[bufferAddress + (uint)(chan * 2 + 1)];
                    _channelVolumes[chan] = _memory.UInt8[bufferAddress + (uint)(chan * 2 + 2)];
                }
                if (_channelOutputMap[0] > 1) {
                    _channelOutputMap[0] = 0;
                }
                if (_channelOutputMap[1] > 1) {
                    _channelOutputMap[1] = 1;
                }
                drive.ApplyChannelControl(_channelOutputMap[0], _channelVolumes[0], _channelOutputMap[1],
                    _channelVolumes[1]);
                break;
            case MscdexIoctlOutputCode.LoadMedia:
                // Load media — no-op for image drives (media is always present when an image is mounted)
                break;
            default:
                if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("MSCDEX IOCTL Output: unknown control code 0x{Code:X2}", controlCode);
                }
                WriteRequestStatus(requestBase, driveEntry, StatusError | (ushort)MscdexErrorCode.InvalidFunction);
                return;
        }
        WriteRequestStatus(requestBase, driveEntry, StatusDone);
    }

    private void HandleReadLong(uint requestBase, MscdexDriveEntry driveEntry) {
        ICdRomDrive drive = driveEntry.Drive;
        ushort bufferOffset = _memory.UInt16[requestBase + MscdexRequestOffsets.IoctlBufferPtrOffset];
        ushort bufferSegment = _memory.UInt16[requestBase + MscdexRequestOffsets.IoctlBufferPtrOffset + 2];
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(bufferSegment, bufferOffset);

        ushort sectorCount = _memory.UInt16[requestBase + MscdexRequestOffsets.ReadLongSectorCountOffset];
        uint startRaw = _memory.UInt32[requestBase + MscdexRequestOffsets.ReadLongStartSectorOffset];
        byte rawFlag = _memory.UInt8[requestBase + MscdexRequestOffsets.ReadLongRawFlagOffset];
        byte addressingMode = _memory.UInt8[requestBase + MscdexRequestOffsets.RequestAddressingModeOffset];

        int startLba;
        if (addressingMode == 0) {
            startLba = (int)startRaw;
        } else {
            // Red Book MSF: low byte=fr, next=sec, next=min
            byte fr = (byte)(startRaw & 0xFF);
            byte sec = (byte)((startRaw >> 8) & 0xFF);
            byte min = (byte)((startRaw >> 16) & 0xFF);
            int totalFrames = min * 60 * 75 + sec * 75 + fr;
            startLba = totalFrames - RedbookPreGapFrames;
            if (startLba < 0) {
                startLba = 0;
            }
        }

        CdSectorMode sectorMode;
        int sectorSize;
        if (rawFlag == 1) {
            sectorMode = CdSectorMode.Raw2352;
            sectorSize = RawSectorSize;
        } else {
            sectorMode = CdSectorMode.CookedData2048;
            sectorSize = CookedSectorSize;
        }

        byte[] sectorBuffer = new byte[sectorCount * sectorSize];
        int bytesRead = drive.Read(startLba, sectorCount, sectorBuffer.AsSpan(), sectorMode);
        if (bytesRead != sectorBuffer.Length) {
            WriteRequestStatus(requestBase, driveEntry, StatusError);
            return;
        }

        _activityNotifier?.NotifyRead(driveEntry.DriveLetter);
        _memory.LoadData(bufferAddress, sectorBuffer);
        WriteRequestStatus(requestBase, driveEntry, StatusDone);
    }

    private bool WriteCurrentPosition(uint bufferAddress, MscdexDriveEntry driveEntry) {
        CdAudioPlayback audioStatus = driveEntry.Drive.GetAudioStatus();
        int currentLba;
        if (audioStatus.Status == CdAudioStatus.Playing) {
            currentLba = audioStatus.CurrentLba;
        } else {
            currentLba = GetAudioState(driveEntry).StartLba;
        }
        byte addressMode = _memory.UInt8[bufferAddress + 1];
        if (addressMode == 0) {
            // HSG/LBA mode: write 4-byte LBA at buffer+2
            _memory.UInt32[bufferAddress + 2] = (uint)currentLba;
            return true;
        }
        if (addressMode == 1) {
            // Red Book MSF mode: write fr, sec, min at buffer+2,3,4; 0x00 at buffer+5
            int totalFrames = currentLba + RedbookPreGapFrames;
            byte fr = (byte)(totalFrames % 75);
            int totalSeconds = totalFrames / 75;
            byte sec = (byte)(totalSeconds % 60);
            byte min = (byte)(totalSeconds / 60);
            _memory.UInt8[bufferAddress + 2] = fr;
            _memory.UInt8[bufferAddress + 3] = sec;
            _memory.UInt8[bufferAddress + 4] = min;
            _memory.UInt8[bufferAddress + 5] = 0;
            return true;
        }
        // addressMode other than 0 (HSG) or 1 (Red Book) returns invalid-function (0x03).
        return false;
    }

    private void WriteChannelControl(uint bufferAddress) {
        // Write identity channel control (each channel maps to itself at full volume)
        for (int chan = 0; chan < ChannelCount; chan++) {
            _memory.UInt8[bufferAddress + (uint)(chan * 2 + 1)] = _channelOutputMap[chan];
            _memory.UInt8[bufferAddress + (uint)(chan * 2 + 2)] = _channelVolumes[chan];
        }
    }

    private void WriteDeviceStatus(uint bufferAddress, ICdRomDrive drive) {
        // Layout:
        //   bit 0  : tray/door open
        //   bit 1  : door locked
        //   bit 2  : supports both raw and cooked sectors (always set)
        //   bit 4  : can read audio (always set)
        //   bit 8  : can control audio (always set)
        //   bit 9  : supports Red Book and HSG addressing (always set)
        //   bit 10 : audio is currently playing
        //   bit 11 : drive is empty (no media)
        // For image-backed drives "no media" is equivalent to the door being open
        // (Eject() opens the door; Insert() closes it).
        uint status = DeviceStatusSupportsRawCooked
                    | DeviceStatusCanReadAudio
                    | DeviceStatusCanControlAudio
                    | DeviceStatusSupportsRedbookHsg;
        if (drive.MediaState.IsDoorOpen) {
            status |= DeviceStatusDoorOpen;
            status |= DeviceStatusDriveEmpty;
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
        // Lead-out in Red Book MSF layout for case 0x0A:
        //   writeb(buffer+3, leadOut.fr)   ← frame at lowest address
        //   writeb(buffer+4, leadOut.sec)
        //   writeb(buffer+5, leadOut.min)
        //   writeb(buffer+6, 0x00)
        (byte min, byte sec, byte fr) = LbaToRedBookMsf(info.LeadOutLba);
        _memory.UInt8[bufferAddress + 3] = fr;
        _memory.UInt8[bufferAddress + 4] = sec;
        _memory.UInt8[bufferAddress + 5] = min;
        _memory.UInt8[bufferAddress + 6] = 0;
    }

    private void WriteAudioTrackInfo(uint bufferAddress, ICdRomDrive drive) {
        byte trackNumber = _memory.UInt8[bufferAddress + 1];
        TableOfContentsEntry? entry = drive.GetTrackInfo(trackNumber);
        if (entry == null) {
            _memory.UInt8[bufferAddress + 2] = 0;
            _memory.UInt8[bufferAddress + 3] = 0;
            _memory.UInt8[bufferAddress + 4] = 0;
            _memory.UInt8[bufferAddress + 5] = 0;
            _memory.UInt8[bufferAddress + 6] = 0;
            return;
        }
        // Track start in Red Book MSF, case 0x0B:
        //   writeb(buffer+2, start.fr)   ← frame at offset 2
        //   writeb(buffer+3, start.sec)
        //   writeb(buffer+4, start.min)
        //   writeb(buffer+5, 0x00)        ← padding
        //   writeb(buffer+6, attr)         ← attribute at offset 6
        (byte min, byte sec, byte fr) = LbaToRedBookMsf(entry.Lba);
        _memory.UInt8[bufferAddress + 2] = fr;
        _memory.UInt8[bufferAddress + 3] = sec;
        _memory.UInt8[bufferAddress + 4] = min;
        _memory.UInt8[bufferAddress + 5] = 0;
        _memory.UInt8[bufferAddress + 6] = entry.Control;
    }

    private void WriteAudioSubchannel(uint bufferAddress, ICdRomDrive drive) {
        CdAudioPlayback audioStatus = drive.GetAudioStatus();
        IReadOnlyList<TableOfContentsEntry> toc = drive.GetTableOfContents();
        SubchannelQSynthesizer synthesizer = new();
        SubchannelQData q = synthesizer.Compute(toc, audioStatus.CurrentLba);

        // Write subchannel response case 0x0C:
        //   writeb(buffer+1, attr)
        //   writeb(buffer+2, track_BCD)
        //   writeb(buffer+3, index)
        //   writeb(buffer+4, rel.min)   writeb(buffer+5, rel.sec)   writeb(buffer+6, rel.fr)
        //   writeb(buffer+7, 0x00)       ← padding between rel and abs
        //   writeb(buffer+8, abs.min)   writeb(buffer+9, abs.sec)   writeb(buffer+10, abs.fr)
        _memory.UInt8[bufferAddress + 1] = q.Attribute;
        _memory.UInt8[bufferAddress + 2] = q.TrackNumberBcd;
        _memory.UInt8[bufferAddress + 3] = q.IndexNumber;
        _memory.UInt8[bufferAddress + 4] = q.RelativeMinute;
        _memory.UInt8[bufferAddress + 5] = q.RelativeSecond;
        _memory.UInt8[bufferAddress + 6] = q.RelativeFrame;
        _memory.UInt8[bufferAddress + 7] = 0;
        _memory.UInt8[bufferAddress + 8] = q.AbsoluteMinute;
        _memory.UInt8[bufferAddress + 9] = q.AbsoluteSecond;
        _memory.UInt8[bufferAddress + 10] = q.AbsoluteFrame;
    }

    private void WriteUpcCode(uint bufferAddress, ICdRomDrive drive) {
        string? upc = drive.GetUpc();
        // Write UPC response case 0x0E:
        //   writeb(buffer+1, attr)
        //   writeb(buffer+2..8, 7 BCD bytes of UPC)
        //   writeb(buffer+9, 0x00)
        _memory.UInt8[bufferAddress + 1] = 0; // attr
        if (!string.IsNullOrEmpty(upc)) {
            // Convert ASCII UPC digits to packed BCD (2 digits per byte)
            for (int i = 0; i < 7; i++) {
                int high = (i * 2 < upc.Length) ? (upc[i * 2] - '0') : 0;
                int low = (i * 2 + 1 < upc.Length) ? (upc[i * 2 + 1] - '0') : 0;
                _memory.UInt8[bufferAddress + (uint)(2 + i)] = (byte)((high << 4) | low);
            }
        }
        _memory.UInt8[bufferAddress + 9] = 0;
    }

    private void WriteAudioStatus(uint bufferAddress, MscdexDriveEntry driveEntry) {
        CdAudioPlayback audioStatus = driveEntry.Drive.GetAudioStatus();
        MscdexAudioState sessionState = GetAudioState(driveEntry);
        bool isPaused = audioStatus.Status == CdAudioStatus.Paused;
        bool hasActiveSession = audioStatus.Status == CdAudioStatus.Playing || isPaused;
        // Layout, case 0x0F:
        //   writeb(buffer+1, pause)          ← single byte, NOT uint16
        //   (buffer+2 not written)
        //   writeb(buffer+3..5, start min/sec/fr) + writeb(buffer+6, 0x00)
        //   writeb(buffer+7..9, end min/sec/fr)   + writeb(buffer+10, 0x00)
        _memory.UInt8[bufferAddress + 1] = isPaused ? (byte)1 : (byte)0;
        int startLba = 0;
        int lengthLba = 0;
        if (hasActiveSession) {
            startLba = sessionState.StartLba;
            lengthLba = sessionState.Length;
        }
        (byte startMin, byte startSec, byte startFr) = LbaToRedBookMsf(startLba);
        _memory.UInt8[bufferAddress + 3] = startMin;
        _memory.UInt8[bufferAddress + 4] = startSec;
        _memory.UInt8[bufferAddress + 5] = startFr;
        _memory.UInt8[bufferAddress + 6] = 0;
        (byte endMin, byte endSec, byte endFr) = LbaToRedBookMsf(lengthLba);
        _memory.UInt8[bufferAddress + 7] = endMin;
        _memory.UInt8[bufferAddress + 8] = endSec;
        _memory.UInt8[bufferAddress + 9] = endFr;
        _memory.UInt8[bufferAddress + 10] = 0;
    }

    private void StopAudio(MscdexDriveEntry driveEntry) {
        CdAudioPlayback audioStatus = driveEntry.Drive.GetAudioStatus();
        MscdexAudioState audioState = GetAudioState(driveEntry);
        if (audioStatus.Status == CdAudioStatus.Playing) {
            driveEntry.Drive.PauseAudio();
            audioState.StartLba = audioStatus.CurrentLba;
            return;
        }

        driveEntry.Drive.StopAudio();
        audioState.StartLba = 0;
        audioState.Length = 0;
    }

    private void ResumeAudio(MscdexDriveEntry driveEntry) {
        CdAudioPlayback audioStatus = driveEntry.Drive.GetAudioStatus();
        if (audioStatus.Status == CdAudioStatus.Paused) {
            driveEntry.Drive.ResumeAudio();
        }
    }

    private void WriteRequestStatus(uint requestBase, MscdexDriveEntry driveEntry, ushort status) {
        _memory.UInt16[requestBase + MscdexRequestOffsets.RequestStatusOffset] = ComposeRequestStatus(driveEntry, status);
    }

    private ushort ComposeRequestStatus(MscdexDriveEntry driveEntry, ushort status) {
        CdAudioPlayback audioStatus = driveEntry.Drive.GetAudioStatus();
        if (audioStatus.Status == CdAudioStatus.Playing) {
            return (ushort)(status | StatusAudioPlaying);
        }
        return status;
    }

    private MscdexAudioState GetAudioState(MscdexDriveEntry driveEntry) {
        if (_audioStates.TryGetValue(driveEntry.DriveLetter, out MscdexAudioState? audioState)) {
            return audioState;
        }

        MscdexAudioState createdAudioState = new MscdexAudioState();
        _audioStates[driveEntry.DriveLetter] = createdAudioState;
        return createdAudioState;
    }

    private static int TranslateRequestSector(byte addressingMode, uint startRaw) {
        if (addressingMode == 0) {
            return (int)startRaw;
        }

        byte frame = (byte)(startRaw & 0xFF);
        byte second = (byte)((startRaw >> 8) & 0xFF);
        byte minute = (byte)((startRaw >> 16) & 0xFF);
        int totalFrames = minute * 60 * 75 + second * 75 + frame;
        int startLba = totalFrames - RedbookPreGapFrames;
        if (startLba < 0) {
            return 0;
        }
        return startLba;
    }

    /// <summary>
    /// Finds the drive at position <paramref name="subunit"/> in the <see cref="_drives"/> list.
    /// </summary>
    private MscdexDriveLookup GetDriveBySubUnit(int subunit) {
        if (subunit < 0 || subunit >= _drives.Count) {
            return MscdexDriveLookup.None;
        }
        return MscdexDriveLookup.From(subunit);
    }

    /// <summary>
    /// Logs a warning and returns an error for any unrecognised AL subfunction value.
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
    private MscdexDriveLookup GetDriveByIndex(int driveIndex) {
        for (int i = 0; i < _drives.Count; i++) {
            if (_drives[i].DriveIndex == driveIndex) {
                return MscdexDriveLookup.From(i);
            }
        }
        return MscdexDriveLookup.None;
    }

    private readonly record struct MscdexDriveLookup(bool IsPresent, int DriveListIndex) {
        public static MscdexDriveLookup None { get; } = new(false, -1);

        public static MscdexDriveLookup From(int driveListIndex) {
            return new MscdexDriveLookup(true, driveListIndex);
        }
    }

    /// <summary>
    /// Converts a logical block address to a 3-tuple of (minute, second, frame) in Red Book MSF format.
    /// The 150-frame pre-gap offset is added before conversion, so LBA 0 maps to MSF 00:02:00.
    /// </summary>
    private static (byte min, byte sec, byte frame) LbaToRedBookMsf(int lba) {
        return LbaToMsf(lba + RedbookPreGapFrames);
    }

    /// <summary>
    /// Converts a frame count to a 3-tuple of (minute, second, frame) without adding any pre-gap offset.
    /// Used for relative-position reporting (position within the current track) where the pre-gap
    /// has already been subtracted.
    /// </summary>
    private static (byte min, byte sec, byte frame) LbaToMsf(int frames) {
        byte fr = (byte)(frames % 75);
        int totalSeconds = frames / 75;
        byte sec = (byte)(totalSeconds % 60);
        byte min = (byte)(totalSeconds / 60);
        return (min, sec, fr);
    }

    private sealed class MscdexAudioState {
        public int StartLba { get; set; }

        public int Length { get; set; }
    }
}
