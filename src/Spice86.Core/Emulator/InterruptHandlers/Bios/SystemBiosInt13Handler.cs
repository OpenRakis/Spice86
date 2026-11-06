using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Bios;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Storage;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
///     INT 13h handler. BIOS disk access functions.
/// </summary>
public class SystemBiosInt13Handler : InterruptHandler {
    // BIOS error codes (returned in AH on failure)
    private const byte ErrorNone = 0x00;
    private const byte ErrorInvalidParameter = 0x01;
    private const byte ErrorSectorNotFound = 0x04;
    private const byte ErrorDriveNotReady = 0x80;
    private const byte ErrorSenseFailed = 0xFF;

    private const byte FloppyType525DoubleSided = 0x01;
    private const byte FloppyType525HighDensity = 0x02;
    private const byte FloppyType35DoubleDensity = 0x03;
    private const byte FloppyType35HighDensity = 0x04;
    private const byte FloppyType35ExtendedDensity = 0x06;

    private readonly IFloppyDriveAccess _floppyAccess;
    private readonly IDriveActivityNotifier _activityNotifier;
    private readonly FloppyDiskTimingService _timingService;

    // Tracks the last operation status per BIOS drive number (index 0=A:, 1=B:)
    private readonly byte[] _lastStatus = new byte[2];

    /// <summary>Returns the status of the last operation on <paramref name="driveNumber"/>
    /// in AH with CF.</summary>
    public (byte ah, bool carryFlag) GetLastStatus(byte driveNumber) {
        if (IsFloppyDrive(driveNumber) && driveNumber < _lastStatus.Length) {
            return (_lastStatus[driveNumber], _lastStatus[driveNumber] != 0);
        }
        return (ErrorNone, false);
    }

    /// <summary>Resets the disk system; always reports success (carry clear).</summary>
    public bool ResetDiskSystem(byte driveNumber) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT13H: Reset Disk DL=0x{Drive:X2}", driveNumber);
        }
        return false;
    }

    /// <summary>
    /// Reset Disk System (AH=0x00). Always succeeds.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x00, Reads = CpuValues.Ah | CpuValues.Dl, Writes = CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ResetDiskSystem(bool calledFromVm) {
        SetCarryFlag(ResetDiskSystem(State.DL), calledFromVm);
    }

    /// <summary>
    /// Get Status of Last Drive Operation (AH=0x01).
    /// Returns the error code stored from the previous operation on that drive.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x01, Reads = CpuValues.Ah | CpuValues.Dl, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetLastStatus(bool calledFromVm) {
        (State.AH, bool carryFlag) = GetLastStatus(State.DL);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Reads <paramref name="sectorCount"/> sectors at CHS
    /// <paramref name="cx"/>/<paramref name="dx"/> of <paramref name="driveNumber"/> into
    /// the buffer at <paramref name="segment"/>:<paramref name="offset"/>; status in AH,
    /// count in AL, CF on error.</summary>
    public (ushort ax, bool carryFlag) ReadSectors(byte driveNumber, byte sectorCount,
        ushort segment, ushort offset, ushort cx, ushort dx) {
        if (sectorCount == 0) {
            return ((ushort)((RecordError(driveNumber, ErrorInvalidParameter).ah << 8) | sectorCount), true);
        }
        ImageDriveNumberMapping readDriveMapping = MapBiosDriveToImageDriveNumber(driveNumber);
        if (!readDriveMapping.IsPresent) {
            return ((ushort)((RecordError(driveNumber, ErrorDriveNotReady).ah << 8) | sectorCount), true);
        }
        byte imageDriveNumber = readDriveMapping.ImageDriveNumber;

        FloppyGeometryResult readGeometryResult = _floppyAccess.GetGeometry(imageDriveNumber);
        if (readGeometryResult.Status != FloppyAccessStatus.Success) {
            return ((ushort)((RecordError(driveNumber, ErrorDriveNotReady).ah << 8) | sectorCount), true);
        }
        int heads = readGeometryResult.Geometry.HeadsPerCylinder;
        int sectorsPerTrack = readGeometryResult.Geometry.SectorsPerTrack;
        int bytesPerSector = readGeometryResult.Geometry.BytesPerSector;

        (int cylinder, int head, int sector) = DecodeChs(cx, dx);
        int lba = ChsToLba(cylinder, head, sector, sectorsPerTrack, heads);
        int byteOffset = lba * bytesPerSector;
        int byteCount = sectorCount * bytesPerSector;

        uint destAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        byte[] transferBuffer = new byte[byteCount];

        _timingService.ScheduleFloppyIoDelay(sectorCount);

        FloppyTransferResult readResult = _floppyAccess.ReadFromImage(imageDriveNumber, byteOffset, transferBuffer, 0, byteCount);
        if (readResult.Status != FloppyAccessStatus.Success) {
            return ((ushort)((RecordError(driveNumber, ErrorSectorNotFound).ah << 8) | sectorCount), true);
        }

        Memory.LoadData(destAddress, transferBuffer);

        if (imageDriveNumber < 26) {
            _activityNotifier.NotifyRead((char)('A' + imageDriveNumber));
        }
        RecordSuccess(driveNumber);
        return ((ushort)((ErrorNone << 8) | sectorCount), false);
    }

    /// <summary>
    /// Read Disk Sectors into Memory (AH=0x02).
    /// Reads AL sectors starting at CHS (CH/CL/DH) from drive DL into the buffer at ES:BX.
    /// Returns an error when AL=0 (zero sectors requested).
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x02, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Cx | CpuValues.Dx | CpuValues.Es | CpuValues.Bx, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.Write, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ReadSectors(bool calledFromVm) {
        (State.AX, bool carryFlag) = ReadSectors(State.DL, State.AL, State.ES, State.BX, State.CX, State.DX);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Writes <paramref name="sectorCount"/> sectors from the buffer at
    /// <paramref name="segment"/>:<paramref name="offset"/> to CHS
    /// <paramref name="cx"/>/<paramref name="dx"/> of <paramref name="driveNumber"/>;
    /// status in AH, count in AL, CF on error.</summary>
    public (ushort ax, bool carryFlag) WriteSectors(byte driveNumber, byte sectorCount,
        ushort segment, ushort offset, ushort cx, ushort dx) {
        if (sectorCount == 0) {
            return ((ushort)((RecordError(driveNumber, ErrorInvalidParameter).ah << 8) | sectorCount), true);
        }
        ImageDriveNumberMapping writeDriveMapping = MapBiosDriveToImageDriveNumber(driveNumber);
        if (!writeDriveMapping.IsPresent) {
            return ((ushort)((RecordError(driveNumber, ErrorDriveNotReady).ah << 8) | sectorCount), true);
        }
        byte imageDriveNumber = writeDriveMapping.ImageDriveNumber;

        FloppyGeometryResult writeGeometryResult = _floppyAccess.GetGeometry(imageDriveNumber);
        if (writeGeometryResult.Status != FloppyAccessStatus.Success) {
            return ((ushort)((RecordError(driveNumber, ErrorDriveNotReady).ah << 8) | sectorCount), true);
        }
        int heads = writeGeometryResult.Geometry.HeadsPerCylinder;
        int sectorsPerTrack = writeGeometryResult.Geometry.SectorsPerTrack;
        int bytesPerSector = writeGeometryResult.Geometry.BytesPerSector;

        (int cylinder, int head, int sector) = DecodeChs(cx, dx);
        int lba = ChsToLba(cylinder, head, sector, sectorsPerTrack, heads);
        int byteOffset = lba * bytesPerSector;
        int byteCount = sectorCount * bytesPerSector;

        byte[] transferBuffer = new byte[byteCount];
        uint srcAddress = MemoryUtils.ToPhysicalAddress(segment, offset);
        for (int i = 0; i < byteCount; i++) {
            transferBuffer[i] = Memory.UInt8[srcAddress + (uint)i];
        }

        _timingService.ScheduleFloppyIoDelay(sectorCount);

        FloppyTransferResult writeResult = _floppyAccess.WriteToImage(imageDriveNumber, byteOffset, transferBuffer, 0, byteCount);
        if (writeResult.Status != FloppyAccessStatus.Success) {
            return ((ushort)((RecordError(driveNumber, ErrorInvalidParameter).ah << 8) | sectorCount), true);
        }

        if (imageDriveNumber < 26) {
            _activityNotifier.NotifyWrite((char)('A' + imageDriveNumber));
        }
        RecordSuccess(driveNumber);
        return ((ushort)((ErrorNone << 8) | sectorCount), false);
    }

    /// <summary>
    /// Write Disk Sectors (AH=0x03).
    /// Writes AL sectors from the buffer at ES:BX to CHS (CH/CL/DH) on drive DL.
    /// Returns an error when AL=0 (zero sectors requested).
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x03, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Cx | CpuValues.Dx | CpuValues.Es | CpuValues.Bx, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.Read, ExecutionPath = ServiceExecutionPath.Direct)]
    public void WriteSectors(bool calledFromVm) {
        (State.AX, bool carryFlag) = WriteSectors(State.DL, State.AL, State.ES, State.BX, State.CX, State.DX);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Returns the geometry of <paramref name="driveNumber"/>; null outputs are
    /// preserved by the caller (BH is always preserved).</summary>
    public (byte ah, byte? al, ushort? cx, ushort? dx, byte? bl, bool carryFlag) GetDriveParameters(
        byte driveNumber) {
        if (!IsFloppyDrive(driveNumber)) {
            // Hard disk fallback
            return (ErrorNone, null, 0xFE3F, 0xFF01, null, false);
        }

        FloppyGeometryResult parametersGeometryResult = _floppyAccess.GetGeometry(driveNumber);
        if (parametersGeometryResult.Status != FloppyAccessStatus.Success) {
            return (RecordError(driveNumber, ErrorDriveNotReady).ah, null, null, null, null, true);
        }
        int totalCylinders = parametersGeometryResult.Geometry.TotalCylinders;
        int headsPerCylinder = parametersGeometryResult.Geometry.HeadsPerCylinder;
        int sectorsPerTrack = parametersGeometryResult.Geometry.SectorsPerTrack;
        int bytesPerSector = parametersGeometryResult.Geometry.BytesPerSector;

        int maxCylinder = totalCylinders - 1;
        int maxHead = headsPerCylinder - 1;
        int maxSector = sectorsPerTrack;

        byte cl = (byte)((maxSector & 0x3F) | ((maxCylinder >> 2) & 0xC0));
        byte ch = (byte)(maxCylinder & 0xFF);
        byte bl = GetFloppyBiosType(totalCylinders, headsPerCylinder, sectorsPerTrack, bytesPerSector);
        RecordSuccess(driveNumber);
        return (ErrorNone, (byte)0, (ushort)(ch << 8 | cl),
            (ushort)(maxHead << 8 | CountMountedFloppyDrives()), bl, false);
    }

    /// <summary>Verifies <paramref name="count"/> sectors of
    /// <paramref name="driveNumber"/>; status in AH with CF.</summary>
    public (byte ah, bool carryFlag) VerifySectors(byte count, byte driveNumber) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT13H: Verify Sectors AL=0x{AL:X2}", count);
        }
        if (count == 0) {
            return RecordError(driveNumber, ErrorInvalidParameter);
        }
        if (IsFloppyDrive(driveNumber)) {
            FloppyGeometryResult verifyGeometryResult = _floppyAccess.GetGeometry(driveNumber);
            if (verifyGeometryResult.Status != FloppyAccessStatus.Success) {
                return RecordError(driveNumber, ErrorInvalidParameter);
            }
            RecordSuccess(driveNumber);
        }
        return (ErrorNone, false);
    }

    /// <summary>
    /// Verify Disk Sectors (AH=0x04). On floppy this is a "read without transfer".
    /// Returns success when the drive is present and AL is non-zero.
    /// </summary>
    /// <remarks>
    /// Shangai II needs this to run.
    /// </remarks>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x04, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Cx | CpuValues.Dx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void VerifySectors(bool calledFromVm) {
        (State.AH, bool carryFlag) = VerifySectors(State.AL, State.DL);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>
    /// Get Drive Parameters (AH=0x08).
    /// Returns geometry for the specified floppy (DL=0x00/0x01) or falls back to a
    /// generic hard-disk response for DL=0x80+.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x08, Reads = CpuValues.Ah | CpuValues.Dl, Writes = CpuValues.Ax | CpuValues.Bx | CpuValues.Cx | CpuValues.Dx | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetDriveParameters(bool calledFromVm) {
        (byte ah, byte? al, ushort? cx, ushort? dx, byte? bl, bool carryFlag) =
            GetDriveParameters(State.DL);
        State.AH = ah;
        if (al is { } alValue) {
            State.AL = alValue;
        }
        if (cx is { } cxValue) {
            State.CX = cxValue;
        }
        if (dx is { } dxValue) {
            State.DX = dxValue;
        }
        if (bl is { } blValue) {
            State.BL = blValue;
        }
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Returns the type of <paramref name="driveNumber"/> in AH; CX/DX are
    /// returned only on the fixed-disk path.</summary>
    public (byte ah, ushort? cx, ushort? dx, bool carryFlag) GetDisketteOrHddType(
        byte driveNumber) {
        if (driveNumber == 0x80) {
            return (0x03, 3, 0x4800, false); // fixed disk
        }

        FloppyGeometryResult diskTypeGeometryResult = _floppyAccess.GetGeometry(driveNumber);
        if (IsFloppyDrive(driveNumber) && diskTypeGeometryResult.Status == FloppyAccessStatus.Success) {
            return (0x01, null, null, false); // floppy without change-line support
        }

        return (ErrorSenseFailed, null, null, true);
    }

    /// <summary>
    /// Get Diskette Type or Check Hard Drive Installed (AH=0x15).
    /// Returns AH=0x01 for a mounted floppy (no change-line support,
    /// which deliberately avoids returning 0x02 to prevent MS-DOS from polling INT 13h AH=0x16),
    /// AH=0x03 for the first hard disk, or sets CF when the drive is not present.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x15, Reads = CpuValues.Ah | CpuValues.Dl, Writes = CpuValues.Ah | CpuValues.Cx | CpuValues.Dx | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetDisketteOrHddType(bool calledFromVm) {
        (byte ah, ushort? cx, ushort? dx, bool carryFlag) = GetDisketteOrHddType(State.DL);
        State.AH = ah;
        if (cx is { } cxValue) {
            State.CX = cxValue;
        }
        if (dx is { } dxValue) {
            State.DX = dxValue;
        }
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Positions the head of <paramref name="driveNumber"/>; status in AH with
    /// CF.</summary>
    public (byte ah, bool carryFlag) SeekToCylinder(byte driveNumber) {
        if (IsFloppyDrive(driveNumber)) {
            FloppyGeometryResult seekGeometryResult = _floppyAccess.GetGeometry(driveNumber);
            if (seekGeometryResult.Status != FloppyAccessStatus.Success) {
                return RecordError(driveNumber, ErrorDriveNotReady);
            }
            RecordSuccess(driveNumber);
        }
        return (ErrorNone, false);
    }

    /// <summary>
    /// Format Track (AH=0x05). Zeros all sectors in the specified track on floppy.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x05, Unknown = true)]
    public void FormatTrack(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (!IsFloppyDrive(driveNumber)) {
            ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }
        FloppyGeometryResult formatGeometryResult = _floppyAccess.GetGeometry(driveNumber);
        if (formatGeometryResult.Status != FloppyAccessStatus.Success) {
            ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }
        int heads = formatGeometryResult.Geometry.HeadsPerCylinder;
        int sectorsPerTrack = formatGeometryResult.Geometry.SectorsPerTrack;
        int bytesPerSector = formatGeometryResult.Geometry.BytesPerSector;
        (int cylinder, int head, int _) = DecodeChs(State.CX, State.DX);
        int lbaStart = ChsToLba(cylinder, head, 1, sectorsPerTrack, heads);
        int byteOffset = lbaStart * bytesPerSector;
        int byteCount = sectorsPerTrack * bytesPerSector;
        byte[] zeros = new byte[byteCount];

        _timingService.ScheduleFloppyIoDelay(sectorsPerTrack);

        FloppyTransferResult formatWriteResult = _floppyAccess.WriteToImage(driveNumber, byteOffset, zeros, 0, byteCount);
        if (formatWriteResult.Status != FloppyAccessStatus.Success) {
            ApplyFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
            return;
        }
        State.AH = ErrorNone;
        State.AL = (byte)sectorsPerTrack;
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Seek to Cylinder (AH=0x0C). Positions the read/write head.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x0C, Reads = CpuValues.Ah | CpuValues.Cx | CpuValues.Dh | CpuValues.Dl, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void SeekToCylinder(bool calledFromVm) {
        (State.AH, bool carryFlag) = SeekToCylinder(State.DL);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Checks whether <paramref name="driveNumber"/> is ready; status in AH with
    /// CF.</summary>
    public (byte ah, bool carryFlag) TestDriveReady(byte driveNumber) {
        FloppyGeometryResult testReadyGeometryResult = _floppyAccess.GetGeometry(driveNumber);
        if (IsFloppyDrive(driveNumber) && testReadyGeometryResult.Status == FloppyAccessStatus.Success) {
            RecordSuccess(driveNumber);
            return (ErrorNone, false);
        }
        return RecordError(driveNumber, ErrorDriveNotReady);
    }

    /// <summary>
    /// Reset Hard Disk Controller (AH=0x0D). Equivalent to AH=0x00.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x0D, Reads = CpuValues.Ah | CpuValues.Dl, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void ResetHardDiskController(bool calledFromVm) {
        SetCarryFlag(ResetDiskSystem(State.DL), calledFromVm);
    }

    /// <summary>
    /// Test Drive Ready (AH=0x10). Returns success when the specified floppy drive has media.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x10, Reads = CpuValues.Ah | CpuValues.Dl, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void TestDriveReady(bool calledFromVm) {
        (State.AH, bool carryFlag) = TestDriveReady(State.DL);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Recalibrates; always reports no error.
    /// </summary>
    public (byte ah, bool carryFlag) Recalibrate() {
        return (ErrorNone, false);
    }

    /// <summary>
    /// Initializes a new instance with floppy image timing, image access and UI notifications.
    /// </summary>
    /// <param name="memory">The emulated memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="floppyAccess">Low-level floppy read/write/geometry provider.</param>
    /// <param name="activityNotifier">Notifier used to surface per-drive read/write activity to the UI.</param>
    /// <param name="timingService">Floppy I/O timing service applied before media transfers.</param>
    /// <param name="loggerService">The logging service implementation.</param>
    public SystemBiosInt13Handler(
        IMemory memory, IFunctionHandlerProvider functionHandlerProvider,
        Stack stack, State state,
        IFloppyDriveAccess floppyAccess,
        IDriveActivityNotifier activityNotifier,
        FloppyDiskTimingService timingService,
        ILogger loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _floppyAccess = floppyAccess;
        _activityNotifier = activityNotifier;
        _timingService = timingService;
    }

    /// <inheritdoc />
    public override byte VectorNumber => 0x13;

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT13H: AH=0x{Function:X2} DL=0x{Drive:X2}", operation, State.DL);
        }

        if (!HasRunnable(operation)) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("BIOS DISK function not provided: AH=0x{Function:X2}", operation);
            }
        }
        Run(operation);
    }


    /// <summary>
    /// Recalibrate (AH=0x11). Moves the head to cylinder 0. Drive number is in DL register. Always returns no eror in AH.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x11, Reads = CpuValues.Ah | CpuValues.Dl, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void Recalibrate(bool calledFromVm) {
        (State.AH, bool carryFlag) = Recalibrate();
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Returns the disk-change line status of <paramref name="driveNumber"/> in
    /// AH with CF.</summary>
    public (byte ah, bool carryFlag) GetDiskChangeLineStatus(byte driveNumber) {
        if (!IsFloppyDrive(driveNumber)) {
            return (ErrorNone, false);
        }
        FloppyGeometryResult changeLineGeometryResult = _floppyAccess.GetGeometry(driveNumber);
        if (changeLineGeometryResult.Status != FloppyAccessStatus.Success) {
            return RecordError(driveNumber, ErrorDriveNotReady);
        }
        RecordSuccess(driveNumber);
        return (ErrorNone, false);
    }

    /// <summary>
    /// Get Disk Change Line Status (AH=0x16). Reports whether media has changed since the last I/O.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x16, Reads = CpuValues.Ah | CpuValues.Dl, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void GetDiskChangeLineStatus(bool calledFromVm) {
        (State.AH, bool carryFlag) = GetDiskChangeLineStatus(State.DL);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Accepts the DASD type; always reports no error.</summary>
    public (byte ah, bool carryFlag) HandleDasdTypeForFormat() {
        return (ErrorNone, false);
    }

    /// <summary>
    /// Set DASD Type for Format (AH=0x17). Accepts disk type in AL, always succeeds.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x17, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dl, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void HandleDasdTypeForFormat(bool calledFromVm) {
        (State.AH, bool carryFlag) = HandleDasdTypeForFormat();
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Accepts the media type geometry; always reports no error.</summary>
    public (byte ah, bool carryFlag) HandleMediaTypeForFormat() {
        return (ErrorNone, false);
    }

    /// <summary>
    /// Set Media Type for Format (AH=0x18). Accepts geometry in CH/CL/DL, always succeeds.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    [InterruptService(0x18, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Cl | CpuValues.Ch | CpuValues.Dl, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.None, ExecutionPath = ServiceExecutionPath.Direct)]
    public void HandleMediaTypeForFormat(bool calledFromVm) {
        (State.AH, bool carryFlag) = HandleMediaTypeForFormat();
        SetCarryFlag(carryFlag, calledFromVm);
    }

    private static (int Cylinder, int Head, int Sector) DecodeChs(ushort cx, ushort dx) {
        int cylinder = (cx >> 8) | ((cx & 0xC0) << 2);
        int head = dx >> 8;
        int sector = cx & 0x3F;
        return (cylinder, head, sector);
    }

    private static int ChsToLba(int cylinder, int head, int sector, int sectorsPerTrack, int heads) {
        return (cylinder * heads + head) * sectorsPerTrack + (sector - 1);
    }

    private static bool IsFloppyDrive(byte driveNumber) {
        return driveNumber < 0x80;
    }

    private static ImageDriveNumberMapping MapBiosDriveToImageDriveNumber(byte biosDriveNumber) {
        if (biosDriveNumber < 0x80) {
            return ImageDriveNumberMapping.From(biosDriveNumber);
        }

        int mapped = 2 + (biosDriveNumber - 0x80);
        if (mapped is < 0 or > byte.MaxValue) {
            return ImageDriveNumberMapping.None;
        }

        return ImageDriveNumberMapping.From((byte)mapped);
    }

    private readonly record struct ImageDriveNumberMapping(bool IsPresent, byte ImageDriveNumber) {
        public static ImageDriveNumberMapping None { get; } = new(false, 0);

        public static ImageDriveNumberMapping From(byte imageDriveNumber) {
            return new ImageDriveNumberMapping(true, imageDriveNumber);
        }
    }

    private int CountMountedFloppyDrives() {
        int count = 0;
        for (byte drive = 0; drive < 2; drive++) {
            FloppyGeometryResult countGeometryResult = _floppyAccess.GetGeometry(drive);
            if (countGeometryResult.Status == FloppyAccessStatus.Success) {
                count++;
            }
        }
        return count;
    }

    private static byte GetFloppyBiosType(int totalCylinders, int headsPerCylinder, int sectorsPerTrack,
        int bytesPerSector) {
        if (bytesPerSector != 512) {
            return 0;
        }

        int totalKilobytes = totalCylinders * headsPerCylinder * sectorsPerTrack * bytesPerSector / 1024;
        switch (totalKilobytes) {
            case 160:
            case 180:
            case 200:
                return 0;
            case 320:
            case 360:
            case 400:
                return FloppyType525DoubleSided;
            case 720:
                return FloppyType35DoubleDensity;
            case 1200:
            case 1520:
                return FloppyType525HighDensity;
            case 1440:
            case 1680:
            case 1720:
            case 1840:
                return FloppyType35HighDensity;
            case 2880:
                return FloppyType35ExtendedDensity;
            default:
                return 0;
        }
    }

    private void ApplyFloppyError(byte driveNumber, byte errorCode, bool calledFromVm) {
        (State.AH, bool carryFlag) = RecordError(driveNumber, errorCode);
        SetCarryFlag(carryFlag, calledFromVm);
    }

    private (byte ah, bool carryFlag) RecordError(byte driveNumber, byte errorCode) {
        if (driveNumber < _lastStatus.Length) {
            _lastStatus[driveNumber] = errorCode;
        }
        return (errorCode, true);
    }

    private void RecordSuccess(byte driveNumber) {
        if (driveNumber < _lastStatus.Length) {
            _lastStatus[driveNumber] = ErrorNone;
        }
    }
}

