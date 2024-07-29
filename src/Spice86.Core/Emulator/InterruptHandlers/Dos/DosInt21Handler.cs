namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Implementation of the DOS INT21H services.
/// </summary>
public class DosInt21Handler : InterruptHandler {
    private readonly Encoding _cp850CharSet;

    private readonly DosMemoryManager _dosMemoryManager;
    private readonly InterruptVectorTable _interruptVectorTable;
    private bool _isCtrlCFlag;

    private StringBuilder _displayOutputBuilder = new();
    private readonly DosFileManager _dosFileManager;
    private readonly List<IVirtualDevice> _devices;
    private readonly Dos _dos;
    private readonly KeyboardInt16Handler _keyboardInt16Handler;
    private readonly IVgaFunctionality _vgaFunctionality;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The emulator memory.</param>
    /// <param name="cpu">The emulated CPU.</param>
    /// <param name="keyboardInt16Handler">The keyboard interrupt handler.</param>
    /// <param name="vgaFunctionality">The high-level VGA functions.</param>
    /// <param name="dos">The DOS kernel.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt21Handler(IMemory memory, Cpu cpu, KeyboardInt16Handler keyboardInt16Handler, IVgaFunctionality vgaFunctionality, Dos dos, ILoggerService loggerService) : base(memory, cpu, loggerService) {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _cp850CharSet = Encoding.GetEncoding("ibm850");
        _dos = dos;
        _vgaFunctionality = vgaFunctionality;
        _keyboardInt16Handler = keyboardInt16Handler;
        _dosMemoryManager = dos.MemoryManager;
        _dosFileManager = dos.FileManager;
        _devices = dos.Devices;
        _interruptVectorTable = new InterruptVectorTable(memory);
        FillDispatchTable();
    }

    private void FillDispatchTable() {
        AddAction(0x00, QuitWithExitCode);
        AddAction(0x02, DisplayOutput);
        AddAction(0x03, ReadCharacterFromStdAux);
        AddAction(0x04, WriteCharacterToStdAux);
        AddAction(0x05, PrinterOutput);
        AddAction(0x06, () => DirectConsoleIo(true));
        AddAction(0x07, DirectStandardInputWithoutEcho);
        AddAction(0x08, DirectStandardInputWithoutEcho);
        AddAction(0x09, PrintString);
        AddAction(0x0B, CheckStandardInputStatus);
        AddAction(0x0C, ClearKeyboardBufferAndInvokeKeyboardFunction);
        AddAction(0x0D, DiskReset);
        AddAction(0x0E, SelectDefaultDrive);
        AddAction(0x19, GetCurrentDefaultDrive);
        AddAction(0x1A, SetDiskTransferAddress);
        AddAction(0x1B, GetAllocationInfoForDefaultDrive);
        AddAction(0x1C, GetAllocationInfoForAnyDrive);
        AddAction(0x25, SetInterruptVector);
        AddAction(0x2A, GetDate);
        AddAction(0x2C, GetTime);
        AddAction(0x2F, GetDiskTransferAddress);
        AddAction(0x30, GetDosVersion);
        AddAction(0x33, GetSetControlBreak);
        AddAction(0x35, GetInterruptVector);
        AddAction(0x36, GetFreeDiskSpace);
        AddAction(0x38, () => SetCountryCode(true));
        AddAction(0x39, () => CreateDirectory(true));
        AddAction(0x3A, () => RemoveDirectory(true));
        AddAction(0x3B, () => ChangeCurrentDirectory(true));
        AddAction(0x3C, () => CreateFileUsingHandle(true));
        AddAction(0x3D, () => OpenFile(true));
        AddAction(0x3E, () => CloseFile(true));
        AddAction(0x3F, () => ReadFile(true));
        AddAction(0x40, () => WriteFileUsingHandle(true));
        AddAction(0x41, () => RemoveFile(true));
        AddAction(0x42, () => MoveFilePointerUsingHandle(true));
        AddAction(0x43, () => GetSetFileAttributes(true));
        AddAction(0x44, () => IoControl(true));
        AddAction(0x45, () => DuplicateFileHandle(true));
        AddAction(0x47, () => GetCurrentDirectory(true));
        AddAction(0x48, () => AllocateMemoryBlock(true));
        AddAction(0x49, () => FreeMemoryBlock(true));
        AddAction(0x4A, () => ModifyMemoryBlock(true));
        AddAction(0x4C, QuitWithExitCode);
        AddAction(0x4E, () => FindFirstMatchingFile(true));
        AddAction(0x4F, () => FindNextMatchingFile(true));
        AddAction(0x51, GetPspAddress);
        AddAction(0x62, GetPspAddress);
        AddAction(0x63, GetLeadByteTable);
    }

    /// <summary>
    /// Get a pointer to the "lead byte" table, for foreign character sets.
    /// This is a table that tells DOS which bytes are the first byte of a double-byte character.
    /// We don't support double-byte characters (yet), so we just return 0.
    /// </summary>
    private void GetLeadByteTable() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET LEAD BYTE TABLE");
        }
        State.AX = 0;
    }

    /// <summary>
    /// Reads a character from the standard auxiliary device (usually the keyboard) and stores it in AL.
    /// </summary>
    public void ReadCharacterFromStdAux() {
        IVirtualDevice? aux = _dos.Devices.Find(x => x is CharacterDevice { Name: "AUX" });
        if (aux is not CharacterDevice stdAux) {
            return;
        }

        using Stream stream = stdAux.OpenStream("r");
        if (stream.CanRead) {
            State.AL = (byte)stream.ReadByte();
        } else {
            State.AL = 0x0;
        }
    }

    /// <summary>
    /// Writes a character from the AL register to the standard auxiliary device.
    /// </summary>
    public void WriteCharacterToStdAux() {
        IVirtualDevice? aux = _dos.Devices.Find(x => x is CharacterDevice { Name: "AUX" });
        if (aux is not CharacterDevice stdAux) {
            return;
        }

        using Stream stream = stdAux.OpenStream("w");
        if (stream.CanWrite) {
            stream.WriteByte(State.AL);
        }
    }

    /// <summary>
    /// Writes a character from the AL register to the printer device.
    /// </summary>
    public void PrinterOutput() {
        IVirtualDevice? prn = _dos.Devices.Find(x => x is CharacterDevice { Name: "PRN" });
        if (prn is not CharacterDevice printer) {
            return;
        }

        using Stream stream = printer.OpenStream("w");
        if (stream.CanWrite) {
            stream.WriteByte(State.AL);
        }
    }

    /// <summary>
    /// Returns 0xFF in AL if input character is available in the standard input, 0 otherwise.
    /// </summary>
    public void CheckStandardInputStatus() {
        CharacterDevice device = _dos.CurrentConsoleDevice;
        if (!device.Attributes.HasFlag(DeviceAttributes.Character | DeviceAttributes.CurrentStdin)) {
            State.AL = 0x0;
            return;
        }

        using Stream stream = device.OpenStream("r");
        if (stream.CanRead) {
            State.AL = 0xFF;
        } else {
            State.AL = 0x0;
        }
    }

    /// <summary>
    /// Copies a character from the standard input to _state.AL, without echo on the standard output.
    /// </summary>
    public void DirectStandardInputWithoutEcho() {
        CharacterDevice device = _dos.CurrentConsoleDevice;
        if (!device.Attributes.HasFlag(DeviceAttributes.Character | DeviceAttributes.CurrentStdin)) {
            State.AL = 0x0;
            return;
        }

        using Stream stream = device.OpenStream("r");
        if (stream.CanRead) {
            int input = stream.ReadByte();
            if (input == -1) {
                State.AL = 0;
            } else {
                State.AL = (byte)input;
            }
        }
    }

    /// <summary>
    /// Creates a directory from the path pointed by DS:DX.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void CreateDirectory(bool calledFromVm) {
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CreateDirectory(GetZeroTerminatedStringAtDsDx());
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Removes a file path, pointed by DS:DX.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void RemoveFile(bool calledFromVm) {
        DosFileOperationResult dosFileOperationResult = _dosFileManager.RemoveFile(GetZeroTerminatedStringAtDsDx());
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Removes a directory, pointed by DS:DX.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void RemoveDirectory(bool calledFromVm) {
        DosFileOperationResult dosFileOperationResult = _dosFileManager.RemoveDirectory(GetZeroTerminatedStringAtDsDx());
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Returns the bytes per sector (in CX), sectors per cluster (in AX), total clusters (in DX), media id (in DS), and drive parameter block address for the default drive (in BX). <br/>
    /// Sets the AH register to 0.
    /// TODO: Implement it for real. This is just a stub that returns the same information as <see cref="GetAllocationInfoForDefaultDrive"/> as we can only mount C: !
    /// </summary>
    public void GetAllocationInfoForAnyDrive() {
        GetAllocationInfoForDefaultDrive();
    }

    /// <summary>
    /// Returns the bytes per sector (in CX), sectors per cluster (in AX), total clusters (in DX), media id (in DS), and drive parameter block address for the default drive (in BX). <br/>
    /// Sets the AH register to 0. <br/>
    /// Notes: always returns the same values.
    /// </summary>
    public void GetAllocationInfoForDefaultDrive() {
        // Bytes per sector
        State.CX = 0x200;
        // Sectors per cluster
        State.AX = 1;
        // Total clusters
        State.DX = 0xEA0;
        // Media Id
        State.DS = 0x8010;
        // From DOSBox source code...
        State.BX = (ushort) (0x8010 + _dosFileManager.DefaultDrive * 9);
        State.AH = 0;
    }

    /// <summary>
    /// Either gets (AL: 0) or sets (AL: not zero) the country code. <br/>
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void SetCountryCode(bool calledFromVm) {
        switch (State.AL) {
            case 0: //Get country specific information
                uint dest = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
                Memory.LoadData(dest, BitConverter.GetBytes((ushort)_dos.CurrentCountryId));
                State.AX = (ushort) (State.BX + 1);
                SetCarryFlag(false, calledFromVm);
                break;
            default: //Set country code
                if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                    LoggerService.Warning("{MethodName}: subFunction is unsupported", nameof(SetCountryCode));
                }
                State.AX = 0;
                SetCarryFlag(false, calledFromVm);
                break;
        }
    }

    /// <summary>
    /// Allocates a memory block of the requested size in BX. <br/>
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0x08 (Insufficient memory).
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void AllocateMemoryBlock(bool calledFromVm) {
        ushort requestedSize = State.BX;
        LoggerService.Verbose("ALLOCATE MEMORY BLOCK {RequestedSize}", requestedSize);
        SetCarryFlag(false, calledFromVm);
        DosMemoryControlBlock? res = _dosMemoryManager.AllocateMemoryBlock(requestedSize);
        if (res == null) {
            LogDosError(calledFromVm);
            // did not find something good, error
            SetCarryFlag(true, calledFromVm);
            DosMemoryControlBlock largest = _dosMemoryManager.FindLargestFree();
            // INSUFFICIENT MEMORY
            State.AX = 0x08;
            State.BX = largest.Size;
            return;
        }
        State.AX = res.UsableSpaceSegment;
    }

    /// <summary>
    /// Changes the current directory to the one pointed by DS:DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void ChangeCurrentDirectory(bool calledFromVm) {
        string newDirectory = GetZeroTerminatedStringAtDsDx();
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET CURRENT DIRECTORY: {NewDirectory}", newDirectory);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.SetCurrentDir(newDirectory);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Clears the keyboard buffer and calls the INT21H function number from AL.
    /// </summary>
    public void ClearKeyboardBufferAndInvokeKeyboardFunction() {
        byte operation = State.AL;
        LoggerService.Debug("CLEAR KEYBOARD AND CALL INT 21 {Operation}", operation);
        Run(operation);
    }

    /// <summary>
    /// Closes a file handle. The handle is in BX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns> 
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void CloseFile(bool calledFromVm) {
        ushort fileHandle = State.BX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("CLOSE FILE handle {FileHandle}", ConvertUtils.ToHex(fileHandle));
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CloseFile(fileHandle);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Creates a file using a handle. The file name is in DS:DX, the file attribute in CX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void CreateFileUsingHandle(bool calledFromVm) {
        string fileName = GetZeroTerminatedStringAtDsDx();
        ushort fileAttribute = State.CX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("CREATE FILE USING HANDLE: {FileName} with attribute {FileAttribute}", fileName, fileAttribute);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CreateFileUsingHandle(fileName, fileAttribute);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Performs an IO control operation. <br/>
    /// TODO: Read from STDIN is not implemented.
    /// TODO: Print to STDOUT only prints to the log, not to the standard output.
    /// </summary>
    /// <returns>
    /// If there is no keycode pending in the keyboard controller buffer, ZF is cleared and AL is set to 0. <br/>
    /// Otherwise, the Zero flag is cleared and the keycode is in AL.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void DirectConsoleIo(bool calledFromVm) {
        byte character = State.DL;
        if (character == 0xFF) {
            LoggerService.Debug("DIRECT CONSOLE IO, INPUT REQUESTED");
            // Read from STDIN, not implemented, return no character ready
            ushort? scancode = _keyboardInt16Handler.GetNextKeyCode();
            if (scancode == null) {
                SetZeroFlag(true, calledFromVm);
                State.AL = 0;
            } else {
                byte ascii = (byte)scancode.Value;
                SetZeroFlag(false, calledFromVm);
                State.AL = ascii;
            }
        } else {
            // Output
            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("DIRECT CONSOLE IO, {Character}, {Ascii}", character, ConvertUtils.ToChar(character));
            }
        }
    }

    /// <summary>
    /// Disk Reset. Not implemented. Does nothing.
    /// </summary>
    public void DiskReset() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("DISK RESET (Nothing to do...)");
        }
    }

    /// <summary>
    /// Puts the character in DL in the internal string buffer named _displayOutputBuilder.<br/>
    /// TODO: This is only a stub that prints nothing on screen.
    /// </summary>
    public void DisplayOutput() {
        byte characterByte = State.DL;
        string character = ConvertSingleDosChar(characterByte);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("PRINT CHR: {CharacterByte} ({Character})", ConvertUtils.ToHex8(characterByte), character);
        }
        if (characterByte == '\r') {
            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("PRINT CHR LINE BREAK: {DisplayOutputBuilder}", _displayOutputBuilder);
            }
            _displayOutputBuilder = new StringBuilder();
        } else if (characterByte != '\n') {
            _displayOutputBuilder.Append(character);
        }
    }

    /// <summary>
    /// Duplicate a file handle. The handle is in BX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void DuplicateFileHandle(bool calledFromVm) {
        ushort fileHandle = State.BX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("DUPLICATE FILE HANDLE. {FileHandle}", fileHandle);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.DuplicateFileHandle(fileHandle);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Finds the first file matching the DOS file spec pointed by DS:DX and the attributes in CX. <br/>
    /// </summary>
    /// <returns>
    /// CF and AX are cleared on success. <br/>
    /// CF is set on error. <br/>
    /// The matching file is returned in the Disk Transfer Area.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void FindFirstMatchingFile(bool calledFromVm) {
        ushort attributes = State.CX;
        string fileSpec = GetZeroTerminatedStringAtDsDx();
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FIND FIRST MATCHING FILE {Attributes}, {FileSpec}", ConvertUtils.ToHex16(attributes), fileSpec);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.FindFirstMatchingFile(fileSpec, attributes);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
        SetAxToZeroOnSuccess(dosFileOperationResult);
    }

    /// <summary>
    /// Undocumented behavior expected by Qbix and Willy Beamish, when FindFirst or FindNext is called.
    /// Comes from DOSBox Staging source code
    /// </summary>
    /// <param name="dosFileOperationResult">The DOS File operation result to check for error status</param>
    private void SetAxToZeroOnSuccess(DosFileOperationResult dosFileOperationResult) {
        if (!dosFileOperationResult.IsError){
            State.AX = 0;
        }
    }

    /// <summary>
    /// Finds the next file matching the DOS file spec given to <see cref="FindFirstMatchingFile"/>. <br/>
    /// </summary>
    /// <returns>
    /// CF and AX are cleared on success. <br/>
    /// CF is set on error. <br/>
    /// The matching file is returned in the Disk Transfer Area.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void FindNextMatchingFile(bool calledFromVm) {
        ushort attributes = State.CX;
        string fileSpec = GetZeroTerminatedStringAtDsDx();
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FIND NEXT MATCHING FILE {Attributes}, {FileSpec}", ConvertUtils.ToHex16(attributes), fileSpec);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.FindNextMatchingFile();
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
        SetAxToZeroOnSuccess(dosFileOperationResult);
    }

    /// <summary>
    /// Free a memory block identified by the block segment in ES. <br/>
    /// </summary>
    /// <returns>
    /// CF and AX are cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0x09 (Invalid memory block address). <br/>
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void FreeMemoryBlock(bool calledFromVm) {
        ushort blockSegment = State.ES;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FREE ALLOCATED MEMORY {BlockSegment}", blockSegment);
        }
        SetCarryFlag(false, calledFromVm);
        if (!_dosMemoryManager.FreeMemoryBlock((ushort)(blockSegment - 1))) {
            LogDosError(calledFromVm);
            SetCarryFlag(true, calledFromVm);
            // INVALID MEMORY BLOCK ADDRESS
            State.AX = 0x09;
        }
    }

    /// <summary>
    /// Gets the current default drive
    /// </summary>
    /// <returns>
    /// AL = current default drive (0x0: A:, 0x1: B:, 0x2: C:, 0x3: D:, ...)
    /// </returns>
    public void GetCurrentDefaultDrive() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET CURRENT DEFAULT DRIVE");
        }
        State.AL = _dosFileManager.DefaultDrive;
    }

    /// <summary>
    /// Gets the current data from the host's DateTime.Now.
    /// </summary>
    /// <returns>
    /// AL = day of the week <br/>
    /// CX = year <br/>
    /// DH = month <br/>
    /// DL = day <br/>
    /// </returns>
    public void GetDate() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET DATE");
        }
        DateTime now = DateTime.Now;
        State.AL = (byte)now.DayOfWeek;
        State.CX = (ushort)now.Year;
        State.DH = (byte)now.Month;
        State.DL = (byte)now.Day;
    }

    /// <summary>
    /// Gets the address of the DTA.
    /// </summary>
    public void GetDiskTransferAddress() {
        State.ES = _dosFileManager.DiskTransferAreaAddressSegment;
        State.BX = _dosFileManager.DiskTransferAreaAddressOffset;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET DTA (DISK TRANSFER ADDRESS) DS:DX {DsDx}",
                ConvertUtils.ToSegmentedAddressRepresentation(State.ES, State.BX));
        }
    }

    /// <summary>
    /// Returns the major, minor, and OEM version of MS-DOS.
    /// </summary>
    public void GetDosVersion() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET DOS VERSION");
        }
        // 5.0
        State.AL = 0x05;
        State.AH = 0x00;
        // FF => MS DOS
        State.BH = 0xFF;
        // DOS OEM KEY 0x00000
        State.BL = 0x00;
        State.CX = 0x00;
    }

    /// <summary>
    /// Returns the amount of free disk space, in clusters, sectors per byte, and number of available clusters.
    /// <remarks>
    /// Always returns 127 sectors per cluster, 512 bytes per sector, 4031 clusters available (~250MB), and 16383 total clusters (~1000MB)
    /// </remarks>
    /// </summary>
    public void GetFreeDiskSpace() {
        byte driveNumber = State.DL;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET FREE DISK SPACE FOR DRIVE {DriveNumber}", driveNumber);
        }
        // 127 sectors per cluster
        State.AX = 0x7F;
        // 512 bytes per sector
        State.CX = 0x200;
        // 4031 clusters available (~250MB)
        State.BX = 0xFBF;
        // 16383 total clusters on disk (~1000MB)
        State.DX = 0x3FFF;
    }

    /// <inheritdoc/>
    public override byte VectorNumber => 0x21;

    /// <summary>
    /// Function 35H returns the address stored in the interrupt vector table for the handler associated with the specified interrupt. <br/>
    /// To call:
    /// <ul>
    ///   <li>AH = 35H</li>
    ///   <li>AL = interrupt number</li>
    /// </ul>
    /// <returns>ES:BX = segment:offset of handler for interrupt specified in AL</returns>
    /// <remarks>Interrupt vectors should always be read with function 35H and set with function 25H (SetInterruptVector). <br/>
    /// Programs should never attempt to read or change interrupt vectors directly in memory.</remarks>
    /// </summary>
    public void GetInterruptVector() {
        byte vectorNumber = State.AL;
        (ushort segment, ushort offset) = _interruptVectorTable[vectorNumber];
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET INTERRUPT VECTOR INT {VectorInt}, got {SegmentedAddress}", ConvertUtils.ToHex8(vectorNumber),
                ConvertUtils.ToSegmentedAddressRepresentation(segment, offset));
        }
        State.ES = segment;
        State.BX = offset;
    }

    /// <summary>
    /// Gets the address of the current Program Segment Prefix.
    /// </summary>
    /// <returns>
    /// The segment of the current PSP in BX.
    /// </returns>
    public void GetPspAddress() {
        ushort pspSegment = _dosMemoryManager.PspSegment;
        State.BX = pspSegment;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET PSP ADDRESS {PspSegment}", ConvertUtils.ToHex16(pspSegment));
        }
    }

    /// <summary>
    /// Gets or sets the Ctrl-C flag. AL: 0 = get, 1 or 2 = set it from DL.
    /// </summary>
    /// <returns>
    /// The Ctrl-C flag in DL if AL is 0. <br/>
    /// </returns>
    /// <exception cref="UnhandledOperationException">If the operation in AL is not supported.</exception>
    public void GetSetControlBreak() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET/SET CTRL-C FLAG");
        }
        byte op = State.AL;
        if (op == 0) {
            // GET
            State.DL = _isCtrlCFlag ? (byte)1 : (byte)0;
        } else if (op is 1 or 2) {
            // SET
            _isCtrlCFlag = State.DL == 1;
        } else {
            throw new UnhandledOperationException(State, "Ctrl-C get/set operation unhandled: " + op);
        }
    }

    /// <summary>
    /// Returns the current MS-DOS time in CH (hour), CL (minute), DH (second), and DL (millisecond) from the host's DateTime.Now.
    /// </summary>
    public void GetTime() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET TIME");
        }
        DateTime now = DateTime.Now;
        State.CH = (byte)now.Hour;
        State.CL = (byte)now.Minute;
        State.DH = (byte)now.Second;
        State.DL = (byte)now.Millisecond;
    }

    /// <summary>
    /// Modifies a memory block identified by the block segment in ES, and sets the new requested size to the value in BX. <br/>
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error. BX is set to zero on error. <br/>
    /// Possible error code in AX: 0x08 (Insufficient memory).
    /// </returns>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    public void ModifyMemoryBlock(bool calledFromVm) {
        ushort requestedSize = State.BX;
        ushort blockSegment = State.ES;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("MODIFY MEMORY BLOCK {Size}, {BlockSegment}", requestedSize, blockSegment);
        }
        SetCarryFlag(false, calledFromVm);
        if (!_dosMemoryManager.ModifyBlock((ushort)(blockSegment - 1), requestedSize)) {
            LogDosError(calledFromVm);
            // An error occurred. Report it as not enough memory.
            SetCarryFlag(true, calledFromVm);
            State.AX = 0x08;
            State.BX = 0;
        }
    }

    /// <summary>
    /// Moves a file using a DOS handle. AL specifies the origin of the move, BX the file handle, CX:DX the offset.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    public void MoveFilePointerUsingHandle(bool calledFromVm) {
        byte originOfMove = State.AL;
        ushort fileHandle = State.BX;
        uint offset = (uint)(State.CX << 16 | State.DX);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("MOVE FILE POINTER USING HANDLE. {OriginOfMove}, {FileHandle}, {Offset}", originOfMove, fileHandle,
            offset);
        }

        DosFileOperationResult dosFileOperationResult =
            _dosFileManager.MoveFilePointerUsingHandle(originOfMove, fileHandle, offset);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Opens the file pointed by DS:DX with the access mode in AL.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    public void OpenFile(bool calledFromVm) {
        string fileName = GetZeroTerminatedStringAtDsDx();
        byte accessMode = State.AL;
        byte rwAccessMode = (byte)(accessMode & 0b111);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("OPEN FILE {FileName} with mode {AccessMod} (rwAccessMode:{RwAccessMode})", fileName, ConvertUtils.ToHex8(accessMode),
                ConvertUtils.ToHex8(rwAccessMode));
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.OpenFile(fileName, rwAccessMode);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Prints a dollar terminated string pointed by DS:DX to the screen at the current cursor position and page.
    /// </summary>
    public void PrintString() {
        ushort segment = State.DS;
        ushort offset = State.DX;
        string str = GetDosString(Memory, segment, offset, '$');

        _vgaFunctionality.WriteString(str);

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("PRINT STRING: {String}", str);
        }
    }

    /// <summary>
    /// Quits the current DOS process and sets the exit code from the value in the AL register. <br/>
    /// TODO: This is only a stub that sets the cpu state <see cref="State.IsRunning"/> property to <c>False</c>, thus ending the emulation loop !
    /// </summary>
    public void QuitWithExitCode() {
        byte exitCode = State.AL;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("QUIT WITH EXIT CODE {ExitCode}", ConvertUtils.ToHex8(exitCode));
        }
        State.IsRunning = false;
    }

    /// <summary>
    /// Reads a file from disk from the file handle in BX, the read length in CX, and the buffer at DS:DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void ReadFile(bool calledFromVm) {
        ushort fileHandle = State.BX;
        ushort readLength = State.CX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("READ FROM FILE handle {FileHandle} length {ReadLength} to {DsDx}", fileHandle, readLength,
                ConvertUtils.ToSegmentedAddressRepresentation(State.DS, State.DX));
        }
        uint targetMemory = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        DosFileOperationResult dosFileOperationResult = _dosFileManager.ReadFile(fileHandle, readLength, targetMemory);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <inheritdoc />
    public override void Run() {
        byte operation = State.AH;
        Run(operation);
    }

    /// <summary>
    /// Sets the default drive from the value in the DL register.
    /// </summary>
    /// <returns>
    /// The number of potentially valid drive letters in AL.
    /// </returns>
    public void SelectDefaultDrive() {
        _dosFileManager.SelectDefaultDrive(State.DL);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SELECT DEFAULT DRIVE {DefaultDrive}", _dosFileManager.DefaultDrive);
        }
        State.AL = _dosFileManager.NumberOfPotentiallyValidDriveLetters;
    }

    /// <summary>
    /// Sets the address of the DTA from DS:DX.
    /// </summary>
    public void SetDiskTransferAddress() {
        ushort segment = State.DS;
        ushort offset = State.DX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET DTA (DISK TRANSFER ADDRESS) DS:DX {DsDxSegmentOffset}",
                ConvertUtils.ToSegmentedAddressRepresentation(segment, offset));
        }
        _dosFileManager.SetDiskTransferAreaAddress(segment, offset);
    }

    /// <summary>
    /// Sets a new interrupt vector from an existing on in the Interrupt Vector Table. <br/>
    /// Be sure to call <see cref="GetInterruptVector"/> first to get the current vector address. <br/>
    /// Params: <br/>
    /// - AL: Vector Number <br/>
    /// - DS:DX: New interrupt vector address
    /// </summary>
    public void SetInterruptVector() {
        byte vectorNumber = State.AL;
        ushort segment = State.DS;
        ushort offset = State.DX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET INTERRUPT VECTOR FOR INT {VectorNumber} at address {SegmentOffset}", ConvertUtils.ToHex(vectorNumber),
                ConvertUtils.ToSegmentedAddressRepresentation(segment, offset));
        }

        SetInterruptVector(vectorNumber, segment, offset);
    }

    /// <summary>
    /// Sets a new interrupt vector from an existing on in the Interrupt Vector Table.
    /// </summary>
    /// <param name="vectorNumber">The vector number the new vector will answer to.</param>
    /// <param name="segment">The address of the new interrupt vector, segment part.</param>
    /// <param name="offset">The address of the new interrupt vector, offset part.</param>
    public void SetInterruptVector(byte vectorNumber, ushort segment, ushort offset) {
        _interruptVectorTable[vectorNumber] = new(segment, offset);
    }

    /// <summary>
    /// Writes a file to disk from the file handle in BX, the file length in CX, and the buffer at DS:DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the called was called by the emulator.</param>
    public void WriteFileUsingHandle(bool calledFromVm) {
        ushort fileHandle = State.BX;
        ushort writeLength = State.CX;
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("WRITE TO FILE handle {FileHandle} length {WriteLength} from {DsDx}", ConvertUtils.ToHex(fileHandle),
                ConvertUtils.ToHex(writeLength), ConvertUtils.ToSegmentedAddressRepresentation(State.DS, State.DX));
        }
        DosFileOperationResult dosFileOperationResult =
            _dosFileManager.WriteFileUsingHandle(fileHandle, writeLength, bufferAddress);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    private string ConvertSingleDosChar(byte characterByte) {
        ReadOnlySpan<byte> sourceAsArray = stackalloc byte[] {characterByte};
        return _cp850CharSet.GetString(sourceAsArray);
    }

    private string ConvertDosChars(IEnumerable<byte> characterBytes) {
        return _cp850CharSet.GetString(characterBytes.ToArray());
    }

    /// <summary>
    /// Gets an ASCIIZ pathname containing the current DOS directory in the address at DS:DI. <br/>
    /// Params: <br/>
    /// DL = drive number (0x0: A:, 0x1: B:, 0x2: C:, 0x3: D:, ...)
    /// </summary>
    /// <remarks>
    /// Does not include a drive, or the initial backslash
    /// </remarks>
    /// <returns>
    /// DS:DI = ASCIZ pathname containing the current DOS directory. <br/>
    /// CF is cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0xF (Invalid Drive).
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void GetCurrentDirectory(bool calledFromVm) {
        uint responseAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.SI);
        DosFileOperationResult result = _dosFileManager.GetCurrentDir(State.DL, out string currentDir);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET CURRENT DIRECTORY {ResponseAddress}: {CurrentDpsDirectory}",
                ConvertUtils.ToSegmentedAddressRepresentation(State.DS, State.SI), currentDir);
        }
        Memory.SetZeroTerminatedString(responseAddress, currentDir, currentDir.Length);
        SetCarryFlag(false, calledFromVm);
        // According to Ralf's Interrupt List, many Microsoft Windows products rely on AX being 0x0100 on success
        if(!result.IsError) {
            State.AX = 0x0100;
        }
        SetStateFromDosFileOperationResult(calledFromVm, result);
    }

    /// <summary>
    /// Gets a string from the memory at the given segment and offset, until the given end character is found.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="segment">The segment part of the start address.</param>
    /// <param name="offset">The offset part of the start address.</param>
    /// <param name="end">The end character. Usually zero.</param>
    /// <returns>The string from memory.</returns>
    public string GetDosString(IMemory memory, ushort segment, ushort offset, char end) {
        uint stringStart = MemoryUtils.ToPhysicalAddress(segment, offset);
        StringBuilder stringBuilder = new();
        List<byte> sourceArray = new();
        while (memory.UInt8[stringStart] != end) {
            sourceArray.Add(memory.UInt8[stringStart++]);
        }
        string convertedString = ConvertDosChars(sourceArray);
        stringBuilder.Append(convertedString);
        return stringBuilder.ToString();
    }

    /// <summary>
    /// Gets (AL: 0) or sets (AL: 1) file attributes for the file identified via its file path at DS:DX.
    /// TODO: The Set File Attributes operation is not implemented.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0x2 (File not found). <br/>
    /// TODO: Always returns that the file is read / write in Get File Attributes mode.
    /// </returns>
    /// <exception cref="UnhandledOperationException">When the operation in the AL Register is not supported.</exception>
    /// <param name="calledFromVm">Whether this was called from internal emulator code.</param>
    public void GetSetFileAttributes(bool calledFromVm) {
        byte op = State.AL;
        string dosFileName = GetZeroTerminatedStringAtDsDx();
        string? fileName = _dosFileManager.TryGetFullHostPathFromDos(dosFileName);
        if (!File.Exists(fileName)) {
            LogDosError(calledFromVm);
            SetCarryFlag(true, calledFromVm);
            // File not found
            State.AX = 0x2;
            return;
        }
        SetCarryFlag(false, calledFromVm);
        switch (op) {
            case 0: {
                    if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                        LoggerService.Verbose("GET FILE ATTRIBUTE {FileName}", fileName);
                    }
                    FileAttributes attributes = File.GetAttributes(fileName);
                    // let's always return the file is read / write
                    bool canWrite = (attributes & FileAttributes.ReadOnly) != FileAttributes.ReadOnly;
                    State.CX = canWrite ? (byte)0 : (byte)1;
                    break;
                }
            case 1: {
                    ushort attribute = State.CX;
                    if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                        LoggerService.Verbose("SET FILE ATTRIBUTE {FileName}, {Attribute}", fileName, attribute);
                    }
                    break;
                }
            default: throw new UnhandledOperationException(State, "getSetFileAttribute operation unhandled: " + op);
        }
    }

    /// <summary>
    /// Gets a zero terminated string from the memory at DS:DX.
    /// </summary>
    /// <returns>The string from memory.</returns>
    public string GetZeroTerminatedStringAtDsDx() {
        return GetDosString(Memory, State.DS, State.DX, '\0');
    }

    /// <summary>
    /// Provides three operations: get device information, set device information, and get logical drive for physical drive. <br/>
    /// <para>
    /// AL = 0: Get device information from the device handle in BX. Returns result in DX. TODO: Implement it entirely. <br/>
    /// AL = 1: Set device information. Does nothing. TODO: Implement it. <br/>
    /// AL = 0xE: Get logical drive for physical drive. Always returns 0 in AL for only one drive. TODO: Update it once we mount more than just C: ! <br/> <br/>
    /// </para>
    /// </summary>
    /// <returns>
    /// Always indicates success by clearing the carry flag.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called from internal emulator code.</param>
    /// <exception cref="UnhandledOperationException">When the IO control operation in the AL Register is not recognized.</exception>
    public void IoControl(bool calledFromVm) {
        byte op = State.AL;
        ushort handle = State.BX;

        SetCarryFlag(false, calledFromVm);

        switch (op) {
            case 0: {
                    if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                        LoggerService.Verbose("GET DEVICE INFORMATION for handle {Handle}", handle);
                    }

                    if (handle < _devices.Count) {
                        IVirtualDevice device = _devices[handle];
                        // @TODO: use the device and it's attributes to fill the response
                    }
                    State.DX = handle < _devices.Count ? (ushort)0x80D3 : (ushort)0x0002;
                    break;
                }
            case 1: {
                    if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                        LoggerService.Verbose("SET DEVICE INFORMATION for handle {Handle} (unimplemented)", handle);
                    }
                    break;
                }
            case 0xE: {
                    ushort driveNumber = State.BL;
                    if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                        LoggerService.Verbose("GET LOGICAL DRIVE FOR PHYSICAL DRIVE {DriveNumber}", driveNumber);
                    }
                    // Only one drive
                    State.AL = 0;
                    break;
                }
            default: throw new UnhandledOperationException(State, $"IO Control operation unhandled: {op}");
        }
    }

    private void LogDosError(bool calledFromVm) {
        string returnMessage = "";
        if (calledFromVm) {
            returnMessage = $"Int will return to {Cpu.PeekReturn()}. ";
        }
        if (LoggerService.IsEnabled(LogEventLevel.Error)) {
            LoggerService.Error("DOS operation failed with an error. {ReturnMessage}. State is {State}", returnMessage, State.ToString());
        }
    }

    private void SetStateFromDosFileOperationResult(bool calledFromVm, DosFileOperationResult dosFileOperationResult) {
        if (dosFileOperationResult.IsError) {
            LogDosError(calledFromVm);
            SetCarryFlag(true, calledFromVm);
        } else {
            SetCarryFlag(false, calledFromVm);
        }
        uint? value = dosFileOperationResult.Value;
        if (value == null) {
            return;
        }
        State.AX = (ushort)value.Value;
        if (dosFileOperationResult.IsValueIsUint32) {
            State.DX = (ushort)(value >> 16);
        }
    }
}
