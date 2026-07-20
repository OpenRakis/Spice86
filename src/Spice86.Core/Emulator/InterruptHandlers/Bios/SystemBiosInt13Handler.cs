namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Function;
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
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _floppyAccess = floppyAccess;
        _activityNotifier = activityNotifier;
        _timingService = timingService;
        FillDispatchTable();
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

    private void FillDispatchTable() {
        AddAction(0x00, () => ResetDiskSystem(true));
        AddAction(0x01, () => GetLastStatus(true));
        AddAction(0x02, () => ReadSectors(true));
        AddAction(0x03, () => WriteSectors(true));
        AddAction(0x04, () => VerifySectors(true));
        AddAction(0x05, () => FormatTrack(true));
        AddAction(0x08, () => GetDriveParameters(true));
        AddAction(0x0C, () => SeekToCylinder(true));
        AddAction(0x0D, () => ResetHardDiskController(true));
        AddAction(0x10, () => TestDriveReady(true));
        AddAction(0x11, () => Recalibrate(true));
        AddAction(0x15, () => GetDisketteOrHddType(true));
        AddAction(0x16, () => GetDiskChangeLineStatus(true));
        AddAction(0x17, () => HandleDasdTypeForFormat(true));
        AddAction(0x18, () => HandleMediaTypeForFormat(true));
    }

    /// <summary>
    /// Reset Disk System (AH=0x00). Always succeeds.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void ResetDiskSystem(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT13H: Reset Disk DL=0x{Drive:X2}", State.DL);
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
    /// Returns an error when AL=0 (zero sectors requested).
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void ReadSectors(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (State.AL == 0) {
            ApplyFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
            return;
        }
        ImageDriveNumberMapping readDriveMapping = MapBiosDriveToImageDriveNumber(driveNumber);
        if (!readDriveMapping.IsPresent) {
            ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }
        byte imageDriveNumber = readDriveMapping.ImageDriveNumber;

        FloppyGeometryResult readGeometryResult = _floppyAccess.GetGeometry(imageDriveNumber);
        if (readGeometryResult.Status != FloppyAccessStatus.Success) {
            ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }
        int heads = readGeometryResult.Geometry.HeadsPerCylinder;
        int sectorsPerTrack = readGeometryResult.Geometry.SectorsPerTrack;
        int bytesPerSector = readGeometryResult.Geometry.BytesPerSector;

        (int cylinder, int head, int sector) = DecodeChs();
        int lba = ChsToLba(cylinder, head, sector, sectorsPerTrack, heads);
        int sectorCount = State.AL;
        int byteOffset = lba * bytesPerSector;
        int byteCount = sectorCount * bytesPerSector;

        uint destAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);
        byte[] transferBuffer = new byte[byteCount];

        _timingService.ScheduleFloppyIoDelay(sectorCount);

        FloppyTransferResult readResult = _floppyAccess.ReadFromImage(imageDriveNumber, byteOffset, transferBuffer, 0, byteCount);
        if (readResult.Status != FloppyAccessStatus.Success) {
            ApplyFloppyError(driveNumber, ErrorSectorNotFound, calledFromVm);
            return;
        }

        Memory.LoadData(destAddress, transferBuffer);

        if (imageDriveNumber < 26) {
            _activityNotifier.NotifyRead((char)('A' + imageDriveNumber));
        }
        State.AH = ErrorNone;
        State.AL = (byte)sectorCount;
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Write Disk Sectors (AH=0x03).
    /// Writes AL sectors from the buffer at ES:BX to CHS (CH/CL/DH) on drive DL.
    /// Returns an error when AL=0 (zero sectors requested).
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void WriteSectors(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (State.AL == 0) {
            ApplyFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
            return;
        }
        ImageDriveNumberMapping writeDriveMapping = MapBiosDriveToImageDriveNumber(driveNumber);
        if (!writeDriveMapping.IsPresent) {
            ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }
        byte imageDriveNumber = writeDriveMapping.ImageDriveNumber;

        FloppyGeometryResult writeGeometryResult = _floppyAccess.GetGeometry(imageDriveNumber);
        if (writeGeometryResult.Status != FloppyAccessStatus.Success) {
            ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }
        int heads = writeGeometryResult.Geometry.HeadsPerCylinder;
        int sectorsPerTrack = writeGeometryResult.Geometry.SectorsPerTrack;
        int bytesPerSector = writeGeometryResult.Geometry.BytesPerSector;

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

        _timingService.ScheduleFloppyIoDelay(sectorCount);

        FloppyTransferResult writeResult = _floppyAccess.WriteToImage(imageDriveNumber, byteOffset, transferBuffer, 0, byteCount);
        if (writeResult.Status != FloppyAccessStatus.Success) {
            ApplyFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
            return;
        }

        if (imageDriveNumber < 26) {
            _activityNotifier.NotifyWrite((char)('A' + imageDriveNumber));
        }
        State.AH = ErrorNone;
        State.AL = (byte)sectorCount;
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Verify Disk Sectors (AH=0x04). On floppy this is a "read without transfer".
    /// Returns success when the drive is present and AL is non-zero.
    /// </summary>
    /// <remarks>
    /// Shangai II needs this to run.
    /// </remarks>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void VerifySectors(bool calledFromVm) {
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT13H: Verify Sectors AL=0x{AL:X2}", State.AL);
        }
        byte driveNumber = State.DL;
        if (State.AL == 0) {
            ApplyFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
            return;
        }
        if (IsFloppyDrive(driveNumber)) {
            FloppyGeometryResult verifyGeometryResult = _floppyAccess.GetGeometry(driveNumber);
            if (verifyGeometryResult.Status != FloppyAccessStatus.Success) {
                ApplyFloppyError(driveNumber, ErrorInvalidParameter, calledFromVm);
                return;
            }
            RecordSuccess(driveNumber);
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

        FloppyGeometryResult parametersGeometryResult = _floppyAccess.GetGeometry(driveNumber);
        if (parametersGeometryResult.Status != FloppyAccessStatus.Success) {
            ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }
        int totalCylinders = parametersGeometryResult.Geometry.TotalCylinders;
        int headsPerCylinder = parametersGeometryResult.Geometry.HeadsPerCylinder;
        int sectorsPerTrack = parametersGeometryResult.Geometry.SectorsPerTrack;
        int bytesPerSector = parametersGeometryResult.Geometry.BytesPerSector;

        int maxCylinder = totalCylinders - 1;
        int maxHead = headsPerCylinder - 1;
        int maxSector = sectorsPerTrack;

        State.AH = ErrorNone;
        State.AL = 0;
        State.DL = (byte)CountMountedFloppyDrives();
        State.DH = (byte)maxHead;
        State.CL = (byte)((maxSector & 0x3F) | ((maxCylinder >> 2) & 0xC0));
        State.CH = (byte)(maxCylinder & 0xFF);
        State.BL = GetFloppyBiosType(totalCylinders, headsPerCylinder, sectorsPerTrack, bytesPerSector);
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Get Diskette Type or Check Hard Drive Installed (AH=0x15).
    /// Returns AH=0x01 for a mounted floppy (no change-line support,
    /// which deliberately avoids returning 0x02 to prevent MS-DOS from polling INT 13h AH=0x16),
    /// AH=0x03 for the first hard disk, or sets CF when the drive is not present.
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

        FloppyGeometryResult diskTypeGeometryResult = _floppyAccess.GetGeometry(driveNumber);
        if (IsFloppyDrive(driveNumber) && diskTypeGeometryResult.Status == FloppyAccessStatus.Success) {
            State.AH = 0x01; // floppy drive without change-line support
            SetCarryFlag(false, calledFromVm);
            return;
        }

        State.AH = ErrorSenseFailed;
        SetCarryFlag(true, calledFromVm);
    }

    /// <summary>
    /// Format Track (AH=0x05). Zeros all sectors in the specified track on floppy.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
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
        (int cylinder, int head, int _) = DecodeChs();
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
    public void SeekToCylinder(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (IsFloppyDrive(driveNumber)) {
            FloppyGeometryResult seekGeometryResult = _floppyAccess.GetGeometry(driveNumber);
            if (seekGeometryResult.Status != FloppyAccessStatus.Success) {
                ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
                return;
            }
            RecordSuccess(driveNumber);
        }
        State.AH = ErrorNone;
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Reset Hard Disk Controller (AH=0x0D). Equivalent to AH=0x00.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void ResetHardDiskController(bool calledFromVm) {
        ResetDiskSystem(calledFromVm);
    }

    /// <summary>
    /// Test Drive Ready (AH=0x10). Returns success when the specified floppy drive has media.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void TestDriveReady(bool calledFromVm) {
        byte driveNumber = State.DL;
        FloppyGeometryResult testReadyGeometryResult = _floppyAccess.GetGeometry(driveNumber);
        if (IsFloppyDrive(driveNumber) && testReadyGeometryResult.Status == FloppyAccessStatus.Success) {
            State.AH = ErrorNone;
            RecordSuccess(driveNumber);
            SetCarryFlag(false, calledFromVm);
        } else {
            ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
        }
    }

    /// <summary>
    /// Recalibrate (AH=0x11). Moves the head to cylinder 0. Drive number is in DL register. Always returns no eror in AH.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void Recalibrate(bool calledFromVm) {
        State.AH = ErrorNone;
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Get Disk Change Line Status (AH=0x16). Reports whether media has changed since the last I/O.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void GetDiskChangeLineStatus(bool calledFromVm) {
        byte driveNumber = State.DL;
        if (!IsFloppyDrive(driveNumber)) {
            State.AH = ErrorNone;
            SetCarryFlag(false, calledFromVm);
            return;
        }
        FloppyGeometryResult changeLineGeometryResult = _floppyAccess.GetGeometry(driveNumber);
        if (changeLineGeometryResult.Status != FloppyAccessStatus.Success) {
            ApplyFloppyError(driveNumber, ErrorDriveNotReady, calledFromVm);
            return;
        }
        State.AH = ErrorNone;
        RecordSuccess(driveNumber);
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Set DASD Type for Format (AH=0x17). Accepts disk type in AL, always succeeds.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void HandleDasdTypeForFormat(bool calledFromVm) {
        State.AH = ErrorNone;
        SetCarryFlag(false, calledFromVm);
    }

    /// <summary>
    /// Set Media Type for Format (AH=0x18). Accepts geometry in CH/CL/DL, always succeeds.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by internal emulator code or not.</param>
    public void HandleMediaTypeForFormat(bool calledFromVm) {
        State.AH = ErrorNone;
        SetCarryFlag(false, calledFromVm);
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
