namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Storage;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
///     INT 13h handler. BIOS disk access functions.
/// </summary>
/// <remarks>
/// In DOSBox, this is INT13_DiskHandler in bios_disk.cpp
/// </remarks>
public class SystemBiosInt13Handler : InterruptHandler {
    // BIOS error codes (returned in AH on failure)
    private const byte ErrorNone = 0x00;
    private const byte ErrorInvalidParameter = 0x01;
    private const byte ErrorDriveNotReady = 0x80;
    private const byte ErrorSenseFailed = 0xFF;

    // INT 13h AH=0x08 BL drive type: 3.5" 1.44 MB floppy
    private const byte FloppyType144MB = 0x04;

    private readonly IFloppyDriveAccess? _floppyAccess;

    // Tracks the last operation status per BIOS drive number (index 0=A:, 1=B:)
    private readonly byte[] _lastStatus = new byte[2];

    /// <summary>
    /// Initializes a new instance without floppy image support.
    /// </summary>
    /// <param name="memory">The emulated memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="loggerService">The logging service implementation.</param>
    public SystemBiosInt13Handler(
        IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state,
        ILoggerService loggerService)
        : this(memory, functionHandlerProvider, stack, state, null, loggerService) {
    }

    /// <summary>
    /// Initializes a new instance with floppy sector access provided by <paramref name="floppyAccess"/>.
    /// </summary>
    /// <param name="memory">The emulated memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="floppyAccess">Low-level floppy read/write/geometry provider (may be null when no floppy images are used).</param>
    /// <param name="loggerService">The logging service implementation.</param>
    public SystemBiosInt13Handler(
        IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state,
        IFloppyDriveAccess? floppyAccess,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _floppyAccess = floppyAccess;
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x13;

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("BIOS INT13H: AH=0x{Function:X2} DL=0x{Drive:X2}", operation, State.DL);
        }

        if (!HasRunnable(operation)) {
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("BIOS DISK function not provided: AH=0x{Function:X2}", operation);
            }
        }
        Run(operation);
    }

    private void FillDispatchTable() {
        AddAction(0x00, () => ResetDiskSystem(true));
        AddAction(0x01, () => GetLastStatus(true));
        AddAction(0x02, () => ReadSectors(true));
        AddAction(0x03, () => WriteSectors(true));
        AddAction(0x04, () => VerifySectors(true));
        AddAction(0x08, () => GetDriveParameters(true));
        AddAction(0x15, () => GetDisketteOrHddType(true));
    }

    /// <summary>
    /// Reset Disk System (AH=0x00). Always succeeds.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void ResetDiskSystem(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("BIOS INT13H: Reset Disk DL=0x{Drive:X2}", State.DL);
        }
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Get Status of Last Drive Operation (AH=0x01).
    /// Returns the error code stored from the previous operation on that drive.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void GetLastStatus(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (IsFloppyDrive(driveNumber) && driveNumber < _lastStatus.Length) {
            State.AH = _lastStatus[driveNumber];
            SetCarryFlag(_lastStatus[driveNumber] != 0, calledFromVm);
        } else {
            State.AH = ErrorNone;
            SetCarryFlag(false, calledFromVm);
        }
    }

    /// <summary>
    /// Read Disk Sectors into Memory (AH=0x02).
    /// Reads AL sectors starting at CHS (CH/CL/DH) from drive DL into the buffer at ES:BX.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void ReadSectors(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (_floppyAccess == null || !IsFloppyDrive(driveNumber)) {
            SetFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }

        if (!_floppyAccess.TryGetGeometry(driveNumber, out int _, out int heads, out int sectorsPerTrack, out int bytesPerSector)) {
            SetFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }

        (int cylinder, int head, int sector) = DecodeChs();
        int lba = ChsToLba(cylinder, head, sector, sectorsPerTrack, heads);
        int sectorCount = State.AL;
        int byteOffset = lba * bytesPerSector;
        int byteCount = sectorCount * bytesPerSector;

        uint destAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);
        byte[] transferBuffer = new byte[byteCount];

        if (!_floppyAccess.TryRead(driveNumber, byteOffset, transferBuffer, 0, byteCount)) {
            SetFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
            return;
        }

        Memory.LoadData(destAddress, transferBuffer);

        State.AH = ErrorNone;
        State.AL = (byte)sectorCount;
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Write Disk Sectors (AH=0x03).
    /// Writes AL sectors from the buffer at ES:BX to CHS (CH/CL/DH) on drive DL.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void WriteSectors(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (_floppyAccess == null || !IsFloppyDrive(driveNumber)) {
            SetFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }

        if (!_floppyAccess.TryGetGeometry(driveNumber, out int _, out int heads, out int sectorsPerTrack, out int bytesPerSector)) {
            SetFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }

        (int cylinder, int head, int sector) = DecodeChs();
        int lba = ChsToLba(cylinder, head, sector, sectorsPerTrack, heads);
        int sectorCount = State.AL;
        int byteOffset = lba * bytesPerSector;
        int byteCount = sectorCount * bytesPerSector;

        byte[] transferBuffer = new byte[byteCount];
        uint srcAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);
        for (int i = 0; i < byteCount; i++) {
            transferBuffer[i] = Memory.UInt8[srcAddress + (uint)i];
        }

        if (!_floppyAccess.TryWrite(driveNumber, byteOffset, transferBuffer, 0, byteCount)) {
            SetFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
            return;
        }

        State.AH = ErrorNone;
        State.AL = (byte)sectorCount;
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Verify Disk Sectors (AH=0x04). Stub — always succeeds for non-zero AL.
    /// </summary>
    /// <remarks>
    /// Shangai II needs this to run.
    /// </remarks>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void VerifySectors(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("BIOS INT13H: Verify Sectors AL=0x{AL:X2}", State.AL);
        }
        if (State.AL == 0) {
            State.AH = ErrorInvalidParameter;
            SetCarryFlag(true, calledFromVm);
            return;
        }
        State.AH = ErrorNone;
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Get Drive Parameters (AH=0x08).
    /// Returns geometry for the specified floppy (DL=0x00/0x01) or falls back to a
    /// generic hard-disk response for DL=0x80+.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void GetDriveParameters(bool calledFromVm) {
        byte driveNumber = State.DL;

        if (!IsFloppyDrive(driveNumber)) {
            // Hard disk fallback
            State.AH = ErrorNone;
            State.DL = 1;
            State.DH = 0xFF;
            State.CL = 0x3F;
            State.CH = 0xFE;
            SetCarryFlag(false, calledFromVm);
            return;
        }

        if (_floppyAccess == null || !_floppyAccess.TryGetGeometry(driveNumber, out int totalCylinders, out int headsPerCylinder, out int sectorsPerTrack, out int _)) {
            SetFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }

        int maxCylinder = totalCylinders - 1;
        int maxHead = headsPerCylinder - 1;
        int maxSector = sectorsPerTrack;

        State.AH = ErrorNone;
        State.DL = 1;
        State.DH = (byte)maxHead;
        State.CL = (byte)((maxSector & 0x3F) | ((maxCylinder >> 2) & 0xC0));
        State.CH = (byte)(maxCylinder & 0xFF);
        State.BL = FloppyType144MB;
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Get Diskette Type or Check Hard Drive Installed (AH=0x15).
    /// Returns AH=0x02 for a mounted floppy, AH=0x03 for the first hard disk,
    /// or sets CF when the drive is not present.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void GetDisketteOrHddType(bool calledFromVm) {
        byte driveNumber = State.DL;

        if (driveNumber == 0x80) {
            State.AH = 0x03; // fixed disk
            State.CX = 3;
            State.DX = 0x4800;
            SetCarryFlag(false, calledFromVm);
            return;
        }

        if (IsFloppyDrive(driveNumber) && _floppyAccess != null &&
            _floppyAccess.TryGetGeometry(driveNumber, out int _, out int _, out int _, out int _)) {
            State.AH = 0x02; // floppy with change-line support
            SetCarryFlag(false, calledFromVm);
            return;
        }

        State.AH = ErrorSenseFailed;
        SetCarryFlag(true, calledFromVm);
    }

    private (int Cylinder, int Head, int Sector) DecodeChs() {
        int cylinder = State.CH | ((State.CL & 0xC0) << 2);
        int head = State.DH;
        int sector = State.CL & 0x3F;
        return (cylinder, head, sector);
    }

    private static int ChsToLba(int cylinder, int head, int sector, int sectorsPerTrack, int heads) {
        return (cylinder * heads + head) * sectorsPerTrack + (sector - 1);
    }

    private static bool IsFloppyDrive(byte driveNumber) {
        return driveNumber < 0x80;
    }

    private void SetFloppyError(byte driveNumber, byte errorCode, bool calledFromVm) {
        State.AH = errorCode;
        if (driveNumber < _lastStatus.Length) {
            _lastStatus[driveNumber] = errorCode;
        }
        SetCarryFlag(true, calledFromVm);
    }

    private void RecordSuccess(byte driveNumber) {
        if (driveNumber < _lastStatus.Length) {
            _lastStatus[driveNumber] = ErrorNone;
        }
    }
}
