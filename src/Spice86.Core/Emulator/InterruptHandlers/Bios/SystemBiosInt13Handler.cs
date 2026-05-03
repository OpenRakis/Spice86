namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.FileSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
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

    // Floppy drive type code for 3.5" 1.44 MB (INT 13h AH=0x08 BL value)
    private const byte FloppyType144MB = 0x04;

    private readonly DosDriveManager? _driveManager;

    // Tracks the last operation status per drive number (0=A, 1=B)
    private readonly byte[] _lastStatus = new byte[2];

    /// <summary>
    /// Initializes a new instance without floppy support (legacy constructor preserved for DI compatibility).
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
    /// Initializes a new instance with floppy drive support via <paramref name="driveManager"/>.
    /// </summary>
    /// <param name="memory">The emulated memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="driveManager">The DOS drive manager used to resolve mounted floppy images.</param>
    /// <param name="loggerService">The logging service implementation.</param>
    public SystemBiosInt13Handler(
        IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state,
        DosDriveManager? driveManager,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _driveManager = driveManager;
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
    /// Reset Disk System (AH=0x00).
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
    /// Returns the error code from the previous INT 13h call for the specified drive.
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
    /// Read Disk Sectors (AH=0x02).
    /// Reads AL sectors from CHS address CH/CL/DH on drive DL into buffer at ES:BX.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void ReadSectors(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (!TryGetFloppyImage(driveNumber, out FloppyDiskDrive? floppy, out byte[]? imageData)) {
            SetFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }

        BiosParameterBlock bpb = floppy.Image!.Bpb;
        (int cylinder, int head, int sector) = DecodeChs();
        int lba = ChsToLba(cylinder, head, sector, bpb.SectorsPerTrack, bpb.NumberOfHeads);
        int sectorCount = State.AL;

        int byteOffset = lba * bpb.BytesPerSector;
        int byteCount = sectorCount * bpb.BytesPerSector;

        if (byteOffset < 0 || byteOffset + byteCount > imageData.Length) {
            SetFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
            return;
        }

        uint destAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);
        Memory.LoadData(destAddress, imageData, byteOffset, byteCount);

        State.AH = ErrorNone;
        State.AL = (byte)sectorCount;
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Write Disk Sectors (AH=0x03).
    /// Writes AL sectors from buffer at ES:BX to CHS address CH/CL/DH on drive DL.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void WriteSectors(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (!TryGetFloppyImage(driveNumber, out FloppyDiskDrive? floppy, out byte[]? imageData)) {
            SetFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }

        BiosParameterBlock bpb = floppy.Image!.Bpb;
        (int cylinder, int head, int sector) = DecodeChs();
        int lba = ChsToLba(cylinder, head, sector, bpb.SectorsPerTrack, bpb.NumberOfHeads);
        int sectorCount = State.AL;

        int byteOffset = lba * bpb.BytesPerSector;
        int byteCount = sectorCount * bpb.BytesPerSector;

        if (byteOffset < 0 || byteOffset + byteCount > imageData.Length) {
            SetFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
            return;
        }

        uint srcAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);
        for (int i = 0; i < byteCount; i++) {
            imageData[byteOffset + i] = Memory.UInt8[srcAddress + (uint)i];
        }

        State.AH = ErrorNone;
        State.AL = (byte)sectorCount;
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Verify Disk Sector (AH=0x04). Stub — always succeeds for non-zero AL.
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
    /// Returns geometry information for the specified floppy or hard drive.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void GetDriveParameters(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (!TryGetFloppyImage(driveNumber, out FloppyDiskDrive? floppy, out byte[]? _)) {
            // Hard disk fallback for 0x80+
            if ((driveNumber & 0x80) != 0) {
                State.AH = ErrorNone;
                State.DL = 1;       // 1 hard drive
                State.DH = 0xFF;    // max head
                State.CL = 0x3F;    // 63 sectors per track (bits 0-5), 0 cylinder hi bits
                State.CH = 0xFE;    // max cylinder low byte (1023 cylinder)
                SetCarryFlag(false, calledFromVm);
            } else {
                SetFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            }
            return;
        }

        BiosParameterBlock bpb = floppy.Image!.Bpb;
        int maxCylinder = bpb.TotalSectors / (bpb.SectorsPerTrack * bpb.NumberOfHeads) - 1;
        int maxHead = bpb.NumberOfHeads - 1;
        int maxSector = bpb.SectorsPerTrack;

        State.AH = ErrorNone;
        State.DL = 1;                   // number of drives
        State.DH = (byte)maxHead;
        // CL bits 0-5: max sector, bits 6-7: high bits of max cylinder
        State.CL = (byte)((maxSector & 0x3F) | ((maxCylinder >> 2) & 0xC0));
        State.CH = (byte)(maxCylinder & 0xFF);
        State.BL = FloppyType144MB;     // drive type: 3.5" 1.44 MB
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Get Diskette Type or Check Hard Drive Installed (AH=0x15).
    /// Returns AH=0x02 (floppy with change line) when a floppy image is mounted,
    /// or AH=0x03 for the first hard disk, or sets CF on error.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void GetDisketteOrHddType(bool calledFromVm) {
        byte driveNumber = State.DL;

        if (driveNumber == 0x80) {
            // First hard disk — report as present with fake sector count
            State.AH = 0x03; // hard disk type
            State.CX = 3;
            State.DX = 0x4800;
            SetCarryFlag(false, calledFromVm);
            return;
        }

        if (IsFloppyDrive(driveNumber) && TryGetFloppyImage(driveNumber, out FloppyDiskDrive? _, out byte[]? _)) {
            State.AH = 0x02; // floppy with change-line support
            SetCarryFlag(false, calledFromVm);
            return;
        }

        // Unknown or unmounted drive
        State.AH = ErrorSenseFailed;
        SetCarryFlag(true, calledFromVm);
    }

    // Decodes CH/CL/DH into (cylinder, head, sector).
    // CL bits 6-7 are the high 2 bits of the cylinder.
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

    private bool TryGetFloppyImage(byte driveNumber, out FloppyDiskDrive? floppy, out byte[]? imageData) {
        floppy = null;
        imageData = null;

        if (_driveManager == null) {
            return false;
        }

        char driveLetter = driveNumber == 0 ? 'A' : 'B';
        if (!_driveManager.TryGetFloppyDrive(driveLetter, out floppy)) {
            return false;
        }

        if (floppy.Image == null) {
            return false;
        }

        imageData = floppy.GetCurrentImageData();
        return imageData != null;
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
