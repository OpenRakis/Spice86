namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
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
using System.Runtime.CompilerServices;
using System.Text;

/// <summary>
/// Implementation of the DOS INT21H services.
/// </summary>
public class DosInt21Handler : InterruptHandler {
    private readonly DosMemoryManager _dosMemoryManager;
    private readonly DosDriveManager _dosDriveManager;
    private readonly DosProgramSegmentPrefixTracker _dosPspTracker;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly DosFileManager _dosFileManager;
    private readonly KeyboardInt16Handler _keyboardInt16Handler;
    private readonly DosStringDecoder _dosStringDecoder;
    private readonly CountryInfo _countryInfo;
    private readonly DosProcessManager _dosProcessManager;
    private readonly DosFileControlBlockManager _fileControlBlockManager;

    private byte _lastDisplayOutputCharacter = 0x0;
    private bool _isCtrlCFlag;
    private readonly Clock _clock;

    /// <summary>
    /// Return code and termination mode for the last executed child process.
    /// </summary>
    private byte _lastReturnCode = 0;
    private DosTerminationMode _lastTerminationMode = DosTerminationMode.Normal;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The emulator memory.</param>
    /// <param name="dosPspTracker">The DOS class used to track the current loaded program.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="keyboardInt16Handler">The keyboard interrupt handler.</param>
    /// <param name="countryInfo">The DOS kernel's global region settings.</param>
    /// <param name="dosStringDecoder">The helper class used to encode/decode DOS strings.</param>
    /// <param name="dosMemoryManager">The DOS class used to manage DOS MCBs.</param>
    /// <param name="dosFileManager">The DOS class responsible for DOS file access.</param>
    /// <param name="dosDriveManager">The DOS class responsible for DOS volumes.</param>
    /// <param name="clock">The class responsible for the clock exposed to DOS programs.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt21Handler(IMemory memory, DosProgramSegmentPrefixTracker dosPspTracker,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        KeyboardInt16Handler keyboardInt16Handler, CountryInfo countryInfo,
        DosStringDecoder dosStringDecoder, DosMemoryManager dosMemoryManager,
        DosProcessManager dosProcessManager, DosFileManager dosFileManager,
        DosDriveManager dosDriveManager, Clock clock, ILoggerService loggerService)
            : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _dosProcessManager = dosProcessManager;
        _countryInfo = countryInfo;
        _dosPspTracker = dosPspTracker;
        _dosStringDecoder = dosStringDecoder;
        _keyboardInt16Handler = keyboardInt16Handler;
        _dosMemoryManager = dosMemoryManager;
        _dosFileManager = dosFileManager;
        _dosDriveManager = dosDriveManager;
        _interruptVectorTable = new InterruptVectorTable(memory);
        _clock = clock;
        _lastReturnCode = 0;
        _lastTerminationMode = DosTerminationMode.Normal;
        _fileControlBlockManager = new DosFileControlBlockManager(memory, dosFileManager, dosDriveManager, loggerService);
        FillDispatchTable();
    }

    /// <summary>
    /// Register the handlers for the DOS INT21H services that we support.
    /// </summary>
    private void FillDispatchTable() {
        AddAction(0x00, QuitWithExitCode);
        AddAction(0x01, ReadCharacterFromStdinWithEcho);
        AddAction(0x02, DisplayOutput);
        AddAction(0x03, ReadCharacterFromStdAux);
        AddAction(0x04, WriteCharacterToStdAux);
        AddAction(0x05, PrinterOutput);
        AddAction(0x06, () => DirectConsoleIo(true));
        AddAction(0x07, DirectStandardInputWithoutEcho);
        AddAction(0x08, DirectStandardInputWithoutEcho);
        AddAction(0x09, PrintString);
        AddAction(0x0A, BufferedInput);
        AddAction(0x0B, CheckStandardInputStatus);
        AddAction(0x0C, ClearKeyboardBufferAndInvokeKeyboardFunction);
        AddAction(0x0D, DiskReset);
        AddAction(0x0E, SelectDefaultDrive);
        AddAction(0x19, GetCurrentDefaultDrive);
        AddAction(0x1A, SetDiskTransferAddress);
        AddAction(0x1B, GetAllocationInfoForDefaultDrive);
        AddAction(0x1C, GetAllocationInfoForAnyDrive);
        AddAction(0x25, SetInterruptVector);
        AddAction(0x26, CreateNewPsp);
        AddAction(0x29, () => ParseFilenameIntoFcb(true));
        AddAction(0x2A, GetDate);
        AddAction(0x2B, SetDate);
        AddAction(0x2C, GetTime);
        AddAction(0x2D, SetTime);
        AddAction(0x2F, GetDiskTransferAddress);
        AddAction(0x30, GetDosVersion);
        AddAction(0x31, TerminateAndStayResident);
        AddAction(0x33, GetSetControlBreak);
        AddAction(0x34, GetInDosFlagAddress);
        AddAction(0x35, GetInterruptVector);
        AddAction(0x36, GetFreeDiskSpace);
        AddAction(0x38, () => SetCountryCode(true));
        AddAction(0x39, () => CreateDirectory(true));
        AddAction(0x3A, () => RemoveDirectory(true));
        AddAction(0x3B, () => ChangeCurrentDirectory(true));
        AddAction(0x3C, () => CreateFileUsingHandle(true));
        AddAction(0x3D, () => OpenFileOrDevice(true));
        AddAction(0x3E, () => CloseFileOrDevice(true));
        AddAction(0x3F, () => ReadFileOrDevice(true));
        AddAction(0x40, () => WriteToFileOrDevice(true));
        AddAction(0x41, () => RemoveFile(true));
        AddAction(0x42, () => MoveFilePointerUsingHandle(true));
        AddAction(0x43, () => GetSetFileAttributes(true));
        AddAction(0x44, () => IoControl(true));
        AddAction(0x45, () => DuplicateFileHandle(true));
        AddAction(0x46, () => ForceDuplicateFileHandle(true));
        AddAction(0x47, () => GetCurrentDirectory(true));
        AddAction(0x48, () => AllocateMemoryBlock(true));
        AddAction(0x49, () => FreeMemoryBlock(true));
        AddAction(0x4A, () => ModifyMemoryBlock(true));
        AddAction(0x4B, () => LoadAndOrExecute(true));
        AddAction(0x4C, QuitWithExitCode);
        AddAction(0x4D, GetReturnCode);
        AddAction(0x4E, () => FindFirstMatchingFile(true));
        AddAction(0x4F, () => FindNextMatchingFile(true));
        AddAction(0x51, GetPspAddress);
        AddAction(0x55, CreateChildPsp);
        AddAction(0x62, GetPspAddress);
        AddAction(0x63, GetLeadByteTable);
        AddAction(0x0F, () => OpenFileUsingFcb(true));
        AddAction(0x10, () => CloseFileUsingFcb(true));
        AddAction(0x11, () => FindFirstMatchingFileUsingFcb(true));
        AddAction(0x12, () => FindNextMatchingFileUsingFcb(true));
        AddAction(0x13, () => DeleteFileUsingFcb(true));
        AddAction(0x14, () => SequentialReadFromFcb(true));
        AddAction(0x15, () => SequentialWriteToFcb(true));
        AddAction(0x16, () => CreateFileUsingFcb(true));
        AddAction(0x17, () => RenameFileUsingFcb(true));
        AddAction(0x21, () => RandomReadFromFcb(true));
        AddAction(0x22, () => RandomWriteToFcb(true));
    }

    /// <summary>
    /// Parses a filename into a File Control Block (FCB).
    /// Function 29H: Parse Filename into FCB
    /// AL = parse control flags
    /// DS:SI = pointer to filename string
    /// ES:DI = pointer to unopened FCB to be filled
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>
    /// AL = result code:
    /// 0 = no wildcards encountered
    /// 1 = wildcards encountered  
    /// 0xFF = drive letter invalid
    /// SI = updated to point past parsed filename
    /// </returns>
    public void ParseFilenameIntoFcb(bool calledFromVm) {
        byte parseControl = State.AL;
        uint sourceAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.SI);
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.DI);

        // Read the filename string from memory (up to 1024 characters, null-terminated)
        string filename = Memory.GetZeroTerminatedString(sourceAddress, 1024);

        if (LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            LoggerService.Verbose("PARSE FILENAME INTO FCB: Control=0x{ParseControl:X2}, Filename='{Filename}', FCB=0x{FcbAddress:X8}",
                parseControl, filename, fcbAddress);
        }

        // Create FCB parser and parse the filename
        DosFileControlBlockParser parser = new DosFileControlBlockParser(Memory, LoggerService);
        byte result = parser.ParseFilename(fcbAddress, parseControl, filename, out int bytesProcessed);

        // Update SI to point past the parsed portion
        State.SI = (ushort)(State.SI + bytesProcessed);
        State.AL = result;

        if (LoggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Parse result: AL={Result}, bytes processed={BytesProcessed}, new SI=0x{NewSI:X4}",
                result, bytesProcessed, State.SI);
        }
    }

    /// <summary>
    /// Reads a character from the standard input device and echoes it to the standard output device.
    /// The character is returned in AL.
    /// </summary>
    /// <remarks>
    /// TODO: Check for Ctrl-C and Ctrl-Break in STDIN, and call INT23H if it happens.
    /// </remarks>
    public void ReadCharacterFromStdinWithEcho() {
        if (!_dosFileManager.TryGetStandardInput(out CharacterDevice? stdIn) ||
            !_dosFileManager.TryGetStandardOutput(out CharacterDevice? stdOut)) {
            State.AL = 0;
            return;
        }

        if (!stdIn.CanRead) {
            State.AL = 0;
            return;
        }

        byte[] inputBuffer = new byte[1];
        int readCount = stdIn.Read(inputBuffer, 0, 1);

        if (readCount < 1) {
            State.AL = 0;
            return;
        }

        byte character = inputBuffer[0];
        State.AL = character;

        // Echo the character to standard output if possible
        if (stdOut.CanWrite) {
            stdOut.Write(character);
        } else if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning("DOS INT21H ReadCharacterFromStdinWithEcho: Cannot echo to standard output device.");
        }
    }

    public void SetDate() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET DATE");
        }

        ushort year = State.CX;
        byte month = State.DH;
        byte day = State.DL;

        if (!_clock.SetDate(year, month, day)) {
            State.AL = 0xFF; // Invalid date
        }
    }

    public void SetTime() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET TIME");
        }

        byte hours = State.CH;
        byte minutes = State.CL;
        byte seconds = State.DH;
        byte hundredths = State.DL;

        if (!_clock.SetTime(hours, minutes, seconds, hundredths)) {
            State.AL = 0xFF; // Invalid time
        }
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
    /// <remarks>
    /// Standard AUX is usually the first serial port (COM1). <br/>
    /// TODO: Check for Ctrl-C and Ctrl-Break in STDIN, and call INT23H if it happens. <br/>
    /// </remarks>
    public void ReadCharacterFromStdAux() {
        if (_dosFileManager.TryGetOpenDeviceWithAttributes(DeviceAttributes.Character,
            out AuxDevice? aux) && aux.CanRead is true) {
            State.AL = (byte)aux.ReadByte();
        } else {
            State.AL = 0x0;
        }
    }

    /// <summary>
    /// Writes a character from the AL register to the standard auxiliary device.
    /// </summary>
    /// <remarks>
    /// TODO: Check for Ctrl-C and Ctrl-Break in STDIN, and call INT23H if it happens. <br/>
    /// </remarks>
    public void WriteCharacterToStdAux() {
        if (_dosFileManager.TryGetOpenDeviceWithAttributes(DeviceAttributes.Character,
            out AuxDevice? aux) && aux.CanWrite is true) {
            aux.WriteByte(State.AL);
        }
    }

    /// <summary>
    /// Writes a character from the AL register to the printer device.
    /// </summary>
    /// <remarks>
    /// Standard printer is usually the first parallel port (LPT1), but may be redirected, as usual with any device in DOS 2+. <br/>
    /// TODO: Check for Ctrl-C and Ctrl-Break in STDIN, and call INT23H if it happens. <br/>
    /// TODO: If the printer is busy, this function will wait. <br/>
    /// </remarks>
    public void PrinterOutput() {
        if (_dosFileManager.TryGetOpenDeviceWithAttributes(DeviceAttributes.Character,
            out PrinterDevice? prn) && prn.CanWrite is true) {
            prn.Write(State.AL);
        } else if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning("DOS INT21H PrinterOutput: Cannot write to printer device.");
        }
    }

    /// <summary>
    /// Returns 0xFF in AL if input character is available in the standard input, 0 otherwise.
    /// </summary>
    public void CheckStandardInputStatus() {
        if (_dosFileManager.TryGetStandardInput(out CharacterDevice? stdIn) &&
            stdIn.Information == ConsoleDevice.InputAvailable) {
            State.AL = 0xFF;
        } else {
            State.AL = 0x0;
        }
    }

    /// <summary>
    /// Copies a character from the standard input to _state.AL, without echo on the standard output.
    /// </summary>
    public void DirectStandardInputWithoutEcho() {
        if (_dosFileManager.TryGetStandardInput(out CharacterDevice? stdIn) &&
            stdIn.CanRead) {
            byte[] bytes = new byte[1];
            var readCount = stdIn.Read(bytes, 0, 1);
            if (readCount < 1) {
                State.AL = 0;
            } else {
                State.AL = bytes[0];
            }
        } else {
            State.AL = 0;
        }
    }

    /// <summary>
    /// Creates a directory from the path pointed by DS:DX.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void CreateDirectory(bool calledFromVm) {
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CreateDirectory(
            _dosStringDecoder.GetZeroTerminatedStringAtDsDx());
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Removes a file path, pointed by DS:DX.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void RemoveFile(bool calledFromVm) {
        DosFileOperationResult dosFileOperationResult = _dosFileManager.RemoveFile(
            _dosStringDecoder.GetZeroTerminatedStringAtDsDx());
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Removes a directory, pointed by DS:DX.
    /// </summary>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void RemoveDirectory(bool calledFromVm) {
        DosFileOperationResult dosFileOperationResult = _dosFileManager.RemoveDirectory(
            _dosStringDecoder.GetZeroTerminatedStringAtDsDx());
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Returns the bytes per sector (in CX), sectors per cluster (in AX), total clusters (in DX), media id (in DS), <br/>
    /// and drive parameter block address for the default drive (in BX). <br/>
    /// Sets the AH register to 0.
    /// </summary>
    /// <remarks>
    /// TODO: Implement it for real. This is just a stub that returns the same information<br/>
    /// as <see cref="GetAllocationInfoForDefaultDrive"/> as we can only mount C: !
    /// </remarks>
    public void GetAllocationInfoForAnyDrive() {
        GetAllocationInfoForDefaultDrive();
    }

    /// <summary>
    /// Returns the bytes per sector (in CX), sectors per cluster (in AX), total clusters (in DX), media id (in DS),<br/>
    /// and drive parameter block address for the default drive (in BX). <br/>
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
        State.BX = (ushort)(0x8010 + _dosDriveManager.CurrentDriveIndex * 9);
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
                Memory.LoadData(dest, BitConverter.GetBytes((ushort)_countryInfo.Country));
                State.AX = (ushort)(State.BX + 1);
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
    /// Allocates a memory block of the requested size in paragraphs in BX. <br/>
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error. Possible error code in AX: 0x08 (Insufficient memory).
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void AllocateMemoryBlock(bool calledFromVm) {
        ushort requestedSizeInParagraphs = State.BX;
        LoggerService.Verbose("ALLOCATE MEMORY BLOCK {RequestedSize}", requestedSizeInParagraphs);
        SetCarryFlag(false, calledFromVm);
        DosMemoryControlBlock? res = _dosMemoryManager.AllocateMemoryBlock(requestedSizeInParagraphs);
        if (res == null) {
            LogDosError(calledFromVm);
            // did not find something good, error
            SetCarryFlag(true, calledFromVm);
            DosMemoryControlBlock largest = _dosMemoryManager.FindLargestFree();
            // INSUFFICIENT MEMORY
            State.AX = (byte)DosErrorCode.InsufficientMemory;
            State.BX = largest.Size;
            return;
        }
        State.AX = res.DataBlockSegment;
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
        string newDirectory = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
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
        if(LoggerService.IsEnabled(LogEventLevel.Debug)) {
            LoggerService.Debug("CLEAR KEYBOARD AND CALL INT 21 {Operation}", operation);
        }
        if (operation is not 0x0 and not 0x6 and not 0x7 and not 0x8 and not 0xA) {
            _keyboardInt16Handler.FlushKeyboardBuffer();
            return;
        }
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
    public void CloseFileOrDevice(bool calledFromVm) {
        ushort fileHandle = State.BX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("CLOSE FILE handle {FileHandle}", ConvertUtils.ToHex(fileHandle));
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CloseFileOrDevice(
            fileHandle);
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
        string fileName = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
        ushort fileAttribute = State.CX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("CREATE FILE USING HANDLE: {FileName} with attribute {FileAttribute}",
                fileName, fileAttribute);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CreateFileUsingHandle(fileName, fileAttribute);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Implements the INT21H function 0x3D, which reads standard input DOS Device and outputs it to the standard output DOS Device. <br/>
    /// </summary>
    /// <remarks>
    /// TODO: Add check for Ctrl-C and Ctrl-Break in STDIN, and call INT23H if it happens.
    /// </remarks>
    public void BufferedInput() {
        uint address = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        DosInputBuffer dosInputBuffer = new DosInputBuffer(Memory, address);
        int readCount = 0;
        if (!_dosFileManager.TryGetStandardInput(out CharacterDevice? standardInput) ||
            !_dosFileManager.TryGetStandardOutput(out CharacterDevice? standardOutput)) {
            return;
        }
        dosInputBuffer.Characters = string.Empty;

        while(State.IsRunning) {
            byte[] inputBuffer = new byte[1];
            readCount = standardInput.Read(inputBuffer, 0, 1);
            if (readCount < 1) {
                break; // No further input available, exit the loop.
            }
            byte c = inputBuffer[0];
            if (c == (byte)AsciiControlCodes.LineFeed) {
                continue;
            }

            if (c == (byte)AsciiControlCodes.Backspace) {
                if (readCount != 0) { //Something to backspace.
                    // STDOUT treats backspace as non-destructive.
                    standardOutput.Write(c);
                    c = Encoding.ASCII.GetBytes(" ")[0];
                    standardOutput.Write(c);
                    c = (byte)AsciiControlCodes.Backspace;
                    standardOutput.Write(c);
                    --readCount;
                }
                continue;
            }
            if (readCount >= dosInputBuffer.Length && c != (byte)AsciiControlCodes.CarriageReturn) { //input buffer full and not CR
                const byte bell = 7;
                standardOutput.Write(bell);
                continue;
            }
            if(standardOutput.CanWrite) {
                standardOutput.Write(c);
            }
            dosInputBuffer.Characters += c;
            if (c == (byte)AsciiControlCodes.CarriageReturn) {
                break;
            }
        }
        dosInputBuffer.ReadCount = (byte)(readCount < 0 ? 0 : (byte)readCount);
    }

    /// <summary>
    /// Performs an IO control operation. <br/>
    /// </summary>
    /// <remarks>
    /// Does not check for Ctrl-C or Ctrl-Break <br/>
    /// </remarks>
    /// <returns>
    /// On output, returns the character in AL, despite the docs saying that nothing is returned. <br/>
    /// On input, AL is set to 0 when no input is available, despite this being not documented. <br/>
    /// If there is no keycode pending in the keyboard controller buffer, ZF is cleared and AL is set to 0. <br/>
    /// Otherwise, the Zero flag is cleared and the keycode is in AL.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void DirectConsoleIo(bool calledFromVm) {
        byte character = State.DL;
        if (character == 0xFF) {
            if(LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("DOS INT21H DirectConsoleIo, INPUT REQUESTED");
            }
            if (_dosFileManager.TryGetStandardInput(out CharacterDevice? stdIn)
                && stdIn.Information == ConsoleDevice.InputAvailable) {
                byte[] bytes = new byte[1];
                var readCount = stdIn.Read(bytes, 0, 1);
                if (readCount < 1) {
                    // No input available, set AL to 0 and ZF to 1.
                    SetZeroFlag(true, calledFromVm);
                    State.AL = 0;
                    return;
                }
                SetZeroFlag(false, calledFromVm);
                State.AL = bytes[0];
            } else {
                SetZeroFlag(true, calledFromVm);
                State.AL = 0;
            }
        } else {
            // Output
            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("DOS INT21H DirectConsoleIo, OUTPUT REQUESTED: {Character}, {Ascii}",
                    character, ConvertUtils.ToChar(character));
            }
            if (_dosFileManager.TryGetStandardOutput(out CharacterDevice? stdOut)
                && stdOut.CanWrite) {
                if(stdOut is ConsoleDevice consoleDeviceBefore) {
                    consoleDeviceBefore.DirectOutput = true;
                }
                stdOut.Write(character);
                if(stdOut is ConsoleDevice consoleDeviceAfter) {
                    consoleDeviceAfter.DirectOutput = false;
                }
                State.AL = character;
            } else if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("DOS INT21H DirectConsoleIo: Cannot write to standard output device.");
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
    /// Writes the character from the DL register to the standard output device. <br/>
    /// </summary>
    /// <returns>
    /// The last character output in the AL register, despite the docs saying that nothing is returned. <br/>
    /// </returns>
    /// <remarks>
    /// TODO: Add check for Ctrl-C and Ctrl-Break in STDIN, and call INT23H if it happens.
    /// </remarks>
    public void DisplayOutput() {
        byte characterByte = State.DL;
        string character = _dosStringDecoder.ConvertSingleDosChar(characterByte);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("PRINT CHR: {CharacterByte} ({Character})",
                ConvertUtils.ToHex8(characterByte), character);
        }
        if (_dosFileManager.TryGetStandardOutput(out CharacterDevice? stdOut) &&
            stdOut.CanWrite) {
            // Write to the standard output device
            stdOut.Write(characterByte);
        } else if(LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning("DOS INT21H DisplayOutput: Cannot write to standard output device.");
        }
        State.AL = _lastDisplayOutputCharacter;
        _lastDisplayOutputCharacter = characterByte;
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
    /// Force duplicates a file handle. The handle is in BX. The new handle is in DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void ForceDuplicateFileHandle(bool calledFromVm) {
        ushort fileHandle = State.BX;
        ushort newHandle = State.DX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FORCE DUPLICATE FILE HANDLE. {FileHandle}, {NewHandle}",
                fileHandle, newHandle);
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.ForceDuplicateFileHandle(fileHandle, newHandle);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
        if (!dosFileOperationResult.IsError) {
            State.AX = State.CX; // Not all sources agree on this, but it seems to be the case.
        }
    }

    /// <summary>
    /// Finds the first file matching the DOS file spec pointed by DS:DX and the attributes in CX. <br/>
    /// </summary>
    /// <remarks>
    /// This also updates the File Control Block (FCB) pointed by DS:DX with the found file information.
    /// </remarks>
    /// <returns>
    /// CF and AX are cleared on success. <br/>
    /// CF is set on error. <br/>
    /// The matching file is returned in the Disk Transfer Area.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void FindFirstMatchingFile(bool calledFromVm) {
        ushort attributes = State.CX;
        string fileSpec = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FIND FIRST MATCHING FILE {Attributes}, {FileSpec}",
                ConvertUtils.ToHex16(attributes), fileSpec);
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
        if (!dosFileOperationResult.IsError) {
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
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FIND NEXT MATCHING FILE");
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
            LoggerService.Verbose("FREE ALLOCATED MEMORY {BlockSegment}",
                ConvertUtils.ToHex16(blockSegment));
        }
        SetCarryFlag(false, calledFromVm);
        if (!_dosMemoryManager.FreeMemoryBlock((ushort)(blockSegment - 1))) {
            LogDosError(calledFromVm);
            SetCarryFlag(true, calledFromVm);
            // INVALID MEMORY BLOCK ADDRESS
            State.AX = (ushort)DosErrorCode.MemoryBlockAddressInvalid;
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
        State.AL = _dosDriveManager.CurrentDriveIndex;
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
        (ushort year, byte month, byte day, byte dayOfWeek) = _clock.GetDate();
        State.AL = dayOfWeek;
        State.CX = year;
        State.DH = month;
        State.DL = day;
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
    /// Creates a new Program Segment Prefix (PSP) for a child process.
    /// Function 26H: Create New PSP
    /// DX = segment where new PSP should be created
    /// </summary>
    public void CreateNewPsp() {
        ushort newPspSegment = State.DX;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("CREATE NEW PSP at segment 0x{NewPspSegment:X4}", newPspSegment);
        }

        DosProgramSegmentPrefix? currentPsp = _dosPspTracker.GetCurrentPsp();
        if (currentPsp == null) {
            if (LoggerService.IsEnabled(LogEventLevel.Error)) {
                LoggerService.Error("Cannot create new PSP: no current PSP found");
            }
            State.AL = 0xFF; // Error
            return;
        }

        // Create the new PSP at the specified segment
        DosProgramSegmentPrefix newPsp = new DosProgramSegmentPrefix(Memory, MemoryUtils.ToPhysicalAddress(newPspSegment, 0));
        
        // Copy basic structure from current PSP
        newPsp.Exit[0] = 0xCD; // INT 20h
        newPsp.Exit[1] = 0x20;
        newPsp.NextSegment = currentPsp.NextSegment;
        newPsp.ParentProgramSegmentPrefix = _dosPspTracker.GetCurrentPspSegment();
        newPsp.EnvironmentTableSegment = currentPsp.EnvironmentTableSegment;
        
        // Initialize file handles to inherit from parent
        for (int i = 0; i < 20; i++) {
            newPsp.Files[i] = currentPsp.Files[i];
        }

        State.AL = 0xF0; // AL destroyed as per DOS specification
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("Created new PSP at segment 0x{NewPspSegment:X4}, parent=0x{ParentSegment:X4}", 
                newPspSegment, newPsp.ParentProgramSegmentPrefix);
        }
    }

    /// <summary>
    /// Creates a child PSP for a child process.
    /// Function 55H: Create Child PSP
    /// DX = segment where child PSP should be created
    /// SI = number of bytes to copy from parent PSP (typically 256)
    /// </summary>
    public void CreateChildPsp() {
        ushort childPspSegment = State.DX;
        ushort copySize = State.SI;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("CREATE CHILD PSP at segment 0x{ChildPspSegment:X4}, copy size {CopySize}", 
                childPspSegment, copySize);
        }

        DosProgramSegmentPrefix? currentPsp = _dosPspTracker.GetCurrentPsp();
        if (currentPsp == null) {
            if (LoggerService.IsEnabled(LogEventLevel.Error)) {
                LoggerService.Error("Cannot create child PSP: no current PSP found");
            }
            State.AL = 0xFF; // Error
            return;
        }

        // Create the child PSP at the specified segment
        DosProgramSegmentPrefix childPsp = new DosProgramSegmentPrefix(Memory, MemoryUtils.ToPhysicalAddress(childPspSegment, 0));
        
        // Copy the specified number of bytes from parent PSP
        uint sourceAddress = currentPsp.BaseAddress;
        uint destAddress = childPsp.BaseAddress;
        ushort actualCopySize = Math.Min(copySize, DosProgramSegmentPrefix.MaxLength);
        
        byte[] pspData = Memory.GetData(sourceAddress, actualCopySize);
        Memory.LoadData(destAddress, pspData);
        
        // Update the parent pointer in the child PSP
        childPsp.ParentProgramSegmentPrefix = _dosPspTracker.GetCurrentPspSegment();
        
        // Set current PSP to the child (as per DOS specification)
        _dosPspTracker.PushPspSegment(childPspSegment);
        
        State.AL = 0xF0; // AL destroyed as per DOS specification
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("Created child PSP at segment 0x{ChildPspSegment:X4}, parent=0x{ParentSegment:X4}", 
                childPspSegment, childPsp.ParentProgramSegmentPrefix);
        }
    }

    /// <summary>
    /// Terminate the current process and either prepare unloading it, or keep it in memory (TSR).
    /// Function 31H: Terminate and Stay Resident
    /// AL = exit code
    /// DX = number of paragraphs to keep resident
    /// </summary>
    public void TerminateAndStayResident() {
        byte exitCode = State.AL;
        ushort paragraphsToKeep = State.DX;
        
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("TERMINATE AND STAY RESIDENT: Exit code {ExitCode}, keep {Paragraphs} paragraphs", 
                exitCode, paragraphsToKeep);
        }

        DosProgramSegmentPrefix? currentPsp = _dosPspTracker.GetCurrentPsp();
        if (currentPsp == null) {
            if (LoggerService.IsEnabled(LogEventLevel.Error)) {
                LoggerService.Error("Cannot terminate: no current PSP found");
            }
            return;
        }

        ushort currentPspSegment = _dosPspTracker.GetCurrentPspSegment();
        
        // Use DosProcessManager for proper TSR termination
        _dosProcessManager.TerminateProcess(currentPspSegment, true, exitCode, paragraphsToKeep);
    }

    /// <summary>
    /// Gets return code from the last executed child process.
    /// Function 4DH: Get Return Code of Child Process
    /// Returns:
    /// AH = termination type (00=normal, 01=Ctrl-C, 02=critical error, 03=TSR)
    /// AL = return code
    /// </summary>
    public void GetReturnCode() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET RETURN CODE: Termination mode {TerminationMode}, return code {ReturnCode}", 
                _lastTerminationMode, _lastReturnCode);
        }

        State.AH = (byte)_lastTerminationMode;
        State.AL = _lastReturnCode;
        
        // Clear the return code after reading it (as per DOS specification)
        _lastReturnCode = 0;
        _lastTerminationMode = DosTerminationMode.Normal;
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
            LoggerService.Verbose("GET FREE DISK SPACE FOR DRIVE {DriveNumber}",
                driveNumber);
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
            LoggerService.Verbose("GET INTERRUPT VECTOR INT {VectorInt}, got {SegmentedAddress}",
                ConvertUtils.ToHex8(vectorNumber),
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
        ushort pspSegment = _dosPspTracker.GetCurrentPspSegment();
        State.BX = pspSegment;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET PSP ADDRESS {PspSegment}",
                ConvertUtils.ToHex16(pspSegment));
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
            throw new UnhandledOperationException(State,
                "Ctrl-C get/set operation unhandled: " + op);
        }
    }

    /// <summary>
    /// Gets the address of the InDOS flag.
    /// </summary>
    private void GetInDosFlagAddress() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET InDOS FLAG ADDRESS");
        }
        State.ES = DosSwappableDataArea.BaseSegment;
        State.BX = DosSwappableDataArea.InDosFlagOffset;
    }

    /// <summary>
    /// Returns the current MS-DOS time in CH (hour), CL (minute), DH (second), and DL (millisecond) from the host's DateTime.Now.
    /// </summary>
    public void GetTime() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET TIME");
        }
        (byte hours, byte minutes, byte seconds, byte hundredths) = _clock.GetTime();
        State.CH = hours;
        State.CL = minutes;
        State.DH = seconds;
        State.DL = hundredths;
    }

    /// <summary>
    /// Modifies a memory block identified by the block segment in ES, <br/>
    /// and sets the new requested size in paragraphs to the value in BX. <br/>
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// On success, AX is set to the size of the MCB, which is at least equal to the requested size.
    /// CF is set on error. The error is in AX. <br/>
    /// Possible error code in AX: 0x08 (Insufficient memory) or 0x09 (MCB block destroyed). <br/>
    /// BX is set to largest free block size in paragraphs on error. <br/>
    /// </returns>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    public void ModifyMemoryBlock(bool calledFromVm) {
        ushort requestedSizeInParagraphs = State.BX;
        ushort blockSegment = State.ES;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("MODIFY MEMORY BLOCK {Size} at {BlockSegment}",
                requestedSizeInParagraphs, ConvertUtils.ToHex16(blockSegment));
        }
        DosErrorCode errorCode = _dosMemoryManager.TryModifyBlock(blockSegment,
            requestedSizeInParagraphs, out DosMemoryControlBlock mcb);
        if (errorCode == DosErrorCode.NoError) {
            State.AX = mcb.Size;
            SetCarryFlag(false, calledFromVm);
        } else {
            LogDosError(calledFromVm);
            SetCarryFlag(true, calledFromVm);
            State.AX = (byte)errorCode;
            State.BX = mcb.Size;
        }
    }

    /// <summary>
    /// Either only load a program or overlay, or load it but do not run it, or load it and run it.
    /// </summary>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    public void LoadAndOrExecute(bool calledFromVm) {
        string programName = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
        DosExecuteMode mode = (DosExecuteMode)State.AL;
        uint parameterBlockAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("LOAD AND/OR EXECUTE: Mode={Mode}, Program={ProgramName}, ParameterBlock=0x{ParameterBlock:X8}", 
                mode, programName, parameterBlockAddress);
        }
        
        // Check for errors from the process manager
        if (!_dosProcessManager.LoadAndOrExecute(mode, programName, parameterBlockAddress)) {
            SetCarryFlag(true, calledFromVm);
            if (LoggerService.IsEnabled(LogEventLevel.Error)) {
                LoggerService.Error("EXEC failed with error code 0x{ErrorCode:X4}", State.AX);
            }
        } else {
            SetCarryFlag(false, calledFromVm);
            if (LoggerService.IsEnabled(LogEventLevel.Information)) {
                LoggerService.Information("EXEC succeeded for program {ProgramName}", programName);
            }
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
        SeekOrigin originOfMove = (SeekOrigin)State.AL;
        ushort fileHandle = State.BX;
        uint offset = (uint)(State.CX << 16 | State.DX);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("MOVE FILE POINTER USING HANDLE. {OriginOfMove}, {FileHandle}, {Offset}", 
                originOfMove, fileHandle, offset);
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
    public void OpenFileOrDevice(bool calledFromVm) {
        string fileName = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
        byte accessMode = State.AL;
        FileAccessMode fileAccessMode = (FileAccessMode)(accessMode & 0b111);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("OPEN FILE {FileName} with mode {AccessMode} : {FileAccessModeByte}", 
                fileName, fileAccessMode,
                ConvertUtils.ToHex8(State.AL));
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.OpenFileOrDevice(
            fileName, fileAccessMode);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
    }

    /// <summary>
    /// Prints a dollar terminated string pointed by DS:DX to the standard output.
    /// </summary>
    public void PrintString() {
        ushort segment = State.DS;
        ushort offset = State.DX;
        string str = _dosStringDecoder.GetDosString(segment, offset, '$');

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("PRINT STRING: {String}", str);
        }
        if (_dosFileManager.TryGetStandardOutput(out CharacterDevice? stdOut)
            && stdOut.CanWrite) {
            // Write to the standard output device
            stdOut.Write(Encoding.ASCII.GetBytes(str));
        } else if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning("DOS INT21H PrintString: Cannot write to standard output device.");
        }
    }

    /// <summary>
    /// Quits the current DOS process and sets the exit code from the value in the AL register.
    /// This function properly handles process termination and cleanup for both normal exit and INT 20h.
    /// </summary>
    public void QuitWithExitCode() {
        byte exitCode = State.AL;
        
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("QUIT WITH EXIT CODE {ExitCode} (0x{ExitCode:X2})", exitCode, exitCode);
        }

        DosProgramSegmentPrefix? currentPsp = _dosPspTracker.GetCurrentPsp();
        if (currentPsp == null) {
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("No current PSP found for termination, stopping emulation");
            }
            State.IsRunning = false;
            return;
        }

        ushort currentPspSegment = _dosPspTracker.GetCurrentPspSegment();
        ushort parentPspSegment = currentPsp.ParentProgramSegmentPrefix;
        
        // Save termination information for parent
        _lastReturnCode = exitCode;
        _lastTerminationMode = DosTerminationMode.Normal;

        // Free the memory allocated to this process
        // The MCB is located 1 paragraph (16 bytes) before the PSP segment
        ushort mcbSegment = (ushort)(currentPspSegment - 1);
        if (!_dosMemoryManager.FreeMemoryBlock(mcbSegment)) {
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("Failed to free memory block for PSP 0x{PspSegment:X4}", currentPspSegment);
            }
        } else if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("Freed memory block for PSP 0x{PspSegment:X4}", currentPspSegment);
        }

        // Free the environment block if it exists
        if (currentPsp.EnvironmentTableSegment != 0) {
            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("Freeing environment block at segment 0x{EnvSegment:X4}", currentPsp.EnvironmentTableSegment);
            }
            // Find and free the environment MCB (also 1 paragraph before the environment segment)
            ushort envMcbSegment = (ushort)(currentPsp.EnvironmentTableSegment - 1);
            if (!_dosMemoryManager.FreeMemoryBlock(envMcbSegment)) {
                if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                    LoggerService.Warning("Failed to free environment block at segment 0x{EnvSegment:X4}", currentPsp.EnvironmentTableSegment);
                }
            }
        }

        // Remove current PSP from the stack
        _dosPspTracker.PopCurrentPspSegment();

        if (parentPspSegment == currentPspSegment) {
            // This is the root process, terminate the emulation
            if (LoggerService.IsEnabled(LogEventLevel.Information)) {
                LoggerService.Information("Root process terminated with exit code {ExitCode}, stopping emulation", exitCode);
            }
            State.IsRunning = false;
        } else {
            if (LoggerService.IsEnabled(LogEventLevel.Information)) {
                LoggerService.Information("Child process at PSP 0x{CurrentPsp:X4} terminated with exit code {ExitCode}, returning to parent PSP 0x{ParentPsp:X4}", 
                    currentPspSegment, exitCode, parentPspSegment);
            }

            // Restore parent's context (CPU state should already be preserved by EXEC)
            // The actual return to parent is handled by the CPU's call/return mechanism
        }
    }

    /// <summary>
    /// Reads a file from disk from the file handle in BX, the read length in CX, and the buffer at DS:DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void ReadFileOrDevice(bool calledFromVm) {
        ushort fileHandle = State.BX;
        ushort readLength = State.CX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("READ FROM FILE handle {FileHandle} length {ReadLength} to {DsDx}",
                fileHandle, readLength,
                ConvertUtils.ToSegmentedAddressRepresentation(State.DS, State.DX));
        }
        uint targetMemory = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        DosFileOperationResult dosFileOperationResult = _dosFileManager.ReadFileOrDevice(
            fileHandle, readLength, targetMemory);
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
        byte driveIndex = State.DL;

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SELECT DEFAULT DRIVE: Index {DriveIndex}", driveIndex);
        }

        // Attempt to change to the requested drive
        bool success = _dosDriveManager.ChangeCurrentDriveEntry(driveIndex);

        if (!success) {
            if (driveIndex > DosDriveManager.MaxDriveCount && LoggerService.IsEnabled(LogEventLevel.Error)) {
                LoggerService.Error("DOS INT21H: Could not set default drive! Unrecognized index in State.DL: {DriveIndex}", driveIndex);
            } else if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("DOS INT21H: Could not set default drive! Drive {DriveIndex} is not mounted", driveIndex);
            }
        } else if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SELECT DEFAULT DRIVE: Successfully changed to {@DefaultDrive}", _dosDriveManager.CurrentDrive);
        }

        // Always return the number of potentially valid drive letters, regardless of success
        State.AL = _dosDriveManager.NumberOfPotentiallyValidDriveLetters;
    }

    /// <summary>
    /// Sets the address of the DTA from DS:DX.
    /// </summary>
    public void SetDiskTransferAddress() {
        ushort segment = State.DS;
        ushort offset = State.DX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET DTA (DISK TRANSFER ADDRESS) DS:DX {DsDxSegmentOffset}",
                ConvertUtils.ToSegmentedAddressRepresentation(
                    segment, offset));
        }
        _dosFileManager.SetDiskTransferAreaAddress(segment, offset);
    }

    /// <summary>
    /// Sets a new interrupt vector from an existing one in the Interrupt Vector Table. <br/>
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
            LoggerService.Verbose("SET INTERRUPT VECTOR FOR INT {VectorNumber} at address {SegmentOffset}",
                ConvertUtils.ToHex(vectorNumber),
                ConvertUtils.ToSegmentedAddressRepresentation(
                    segment, offset));
        }

        SetInterruptVector(vectorNumber, segment, offset);
    }

    /// <summary>
    /// Sets a new interrupt vector from an existing one in the Interrupt Vector Table.
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
    public void WriteToFileOrDevice(bool calledFromVm) {
        ushort fileHandle = State.BX;
        ushort writeLength = State.CX;
        uint bufferAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("WRITE TO FILE handle {FileHandle} length {WriteLength} from {DsDx}",
                ConvertUtils.ToHex(fileHandle),
                ConvertUtils.ToHex(writeLength),
                ConvertUtils.ToSegmentedAddressRepresentation(State.DS, State.DX));
        }
        DosFileOperationResult dosFileOperationResult =
            _dosFileManager.WriteToFileOrDevice(fileHandle, writeLength, bufferAddress);
        SetStateFromDosFileOperationResult(calledFromVm, dosFileOperationResult);
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
                ConvertUtils.ToSegmentedAddressRepresentation(
                    State.DS, State.SI), currentDir);
        }
        Memory.SetZeroTerminatedString(responseAddress, currentDir, currentDir.Length);
        // According to Ralf's Interrupt List, many Microsoft Windows products rely on AX being 0x0100 on success
        if (!result.IsError) {
            State.AX = 0x0100;
        }
        SetStateFromDosFileOperationResult(calledFromVm, result);
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
        string dosFileName = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
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
                        LoggerService.Verbose("SET FILE ATTRIBUTE {FileName}, {Attribute}",
                            fileName, attribute);
                    }
                    break;
                }
            default: throw new UnhandledOperationException(State, "getSetFileAttribute operation unhandled: " + op);
        }
    }

    /// <summary>
    /// Provides MS-DOS drivers based IOCTL operations, which are device-driver specific operations.
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>A <see cref="DosFileOperationResult"/> with details about the result of the operation.</returns>
    /// <remarks>
    /// Supports the following operations based on the AL register:
    /// <list type="bullet">
    ///   <item><description>AL = 0x00: Get Device Information from handle in BX. Returns information in DX.</description></item>
    ///   <item><description>AL = 0x01: Set Device Information using handle in BX and data in DL.</description></item>
    ///   <item><description>AL = 0x02: Read from Device Control Channel using handle in BX.</description></item>
    ///   <item><description>AL = 0x03: Write to Device Control Channel using handle in BX.</description></item>
    ///   <item><description>AL = 0x06: Get Input Status for handle in BX. Returns status in AL (0xFF=ready, 0x00=not ready).</description></item>
    ///   <item><description>AL = 0x07: Get Output Status for handle in BX. Returns status in AL (0xFF=ready, 0x00=not ready).</description></item>
    ///   <item><description>AL = 0x08: Check if block device in BL is removable. Returns AL=0 for removable, AL=1 for fixed.</description></item>
    ///   <item><description>AL = 0x09: Check if block device in BL is remote. Returns DX with device attributes.</description></item>
    ///   <item><description>AL = 0x0B: Set sharing retry count. DX=retry count.</description></item>
    ///   <item><description>AL = 0x0D: Generic block device request for drive in BL. Command in CL, parameter block at DS:DX.</description></item>
    ///   <item><description>AL = 0x0E: Get Logical Drive Map for drive in BL. Returns physical drive in AL.</description></item>
    /// </list>
    /// </remarks>
    public void IoControl(bool calledFromVm) {
        DosFileOperationResult result = _dosFileManager.IoControl(State);
        SetStateFromDosFileOperationResult(calledFromVm, result);
    }

    private void LogDosError(bool calledFromVm, [CallerMemberName] string? callerName = null) {
        string returnMessage = "";
        if (calledFromVm) {
            returnMessage = $"Int will return to {FunctionHandlerProvider.FunctionHandlerInUse.PeekReturn()}. ";
        }
        if (LoggerService.IsEnabled(LogEventLevel.Error)) {
            LoggerService.Error("DOS operation from {CallerName} failed with an error. {ReturnMessage}. State is {State}",
                callerName, returnMessage, State.ToString());
        }
    }

    private void SetStateFromDosFileOperationResult(bool calledFromVm,
        DosFileOperationResult dosFileOperationResult, [CallerMemberName] string? callerName = null) {
        if (dosFileOperationResult.IsError) {
            LogDosError(calledFromVm, callerName);
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

    /// <summary>
    /// Opens a file using an FCB (DOS function 0x0F).
    /// DS:DX = pointer to unopened FCB
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0x00 if successful, 0xFF if failed</returns>
    public void OpenFileUsingFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("OPEN FILE USING FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        bool success = _fileControlBlockManager.OpenFile(fcbAddress);
        State.AL = success ? (byte)0x00 : (byte)0xFF;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Open result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Closes a file using an FCB (DOS function 0x10).
    /// DS:DX = pointer to opened FCB
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0x00 if successful, 0xFF if failed</returns>
    public void CloseFileUsingFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("CLOSE FILE USING FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        bool success = _fileControlBlockManager.CloseFile(fcbAddress);
        State.AL = success ? (byte)0x00 : (byte)0xFF;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Close result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Finds the first file matching an FCB pattern (DOS function 0x11).
    /// DS:DX = pointer to unopened FCB with search pattern
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0x00 if successful, 0xFF if no files found</returns>
    public void FindFirstMatchingFileUsingFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FIND FIRST MATCHING FILE USING FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        bool success = _fileControlBlockManager.FindFirstFile(fcbAddress);
        State.AL = success ? (byte)0x00 : (byte)0xFF;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB FindFirst result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Finds the next file matching an FCB pattern (DOS function 0x12).
    /// DS:DX = pointer to FCB from previous FindFirst
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0x00 if successful, 0xFF if no more files found</returns>
    public void FindNextMatchingFileUsingFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FIND NEXT MATCHING FILE USING FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        bool success = _fileControlBlockManager.FindNextFile(fcbAddress);
        State.AL = success ? (byte)0x00 : (byte)0xFF;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB FindNext result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Deletes a file using an FCB (DOS function 0x13).
    /// DS:DX = pointer to unopened FCB
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0x00 if successful, 0xFF if failed</returns>
    public void DeleteFileUsingFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("DELETE FILE USING FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        bool success = _fileControlBlockManager.DeleteFile(fcbAddress);
        State.AL = success ? (byte)0x00 : (byte)0xFF;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Delete result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Performs sequential read from an FCB (DOS function 0x14).
    /// DS:DX = pointer to opened FCB
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0 (success), 1 (EOF), 2 (segment wrap), 3 (partial record)</returns>
    public void SequentialReadFromFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SEQUENTIAL READ FROM FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        byte result = _fileControlBlockManager.ReadRecord(fcbAddress, false);
        State.AL = result;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Sequential Read result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Performs sequential write to an FCB (DOS function 0x15).
    /// DS:DX = pointer to opened FCB
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0 (success), 1 (disk full), 2 (segment wrap)</returns>
    public void SequentialWriteToFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SEQUENTIAL WRITE TO FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        byte result = _fileControlBlockManager.WriteRecord(fcbAddress, false);
        State.AL = result;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Sequential Write result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Creates or truncates a file using an FCB (DOS function 0x16).
    /// DS:DX = pointer to unopened FCB
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0x00 if successful, 0xFF if failed</returns>
    public void CreateFileUsingFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("CREATE FILE USING FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        bool success = _fileControlBlockManager.CreateFile(fcbAddress);
        State.AL = success ? (byte)0x00 : (byte)0xFF;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Create result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Renames a file using an FCB (DOS function 0x17).
    /// DS:DX = pointer to special FCB with old name in bytes 0-10 and new name in bytes 17-27
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0x00 if successful, 0xFF if failed</returns>
    public void RenameFileUsingFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RENAME FILE USING FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        bool success = _fileControlBlockManager.RenameFile(fcbAddress);
        State.AL = success ? (byte)0x00 : (byte)0xFF;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Rename result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Performs random read from an FCB (DOS function 0x21).
    /// DS:DX = pointer to opened FCB with RandomRecord field set
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0 (success), 1 (EOF), 2 (segment wrap), 3 (partial record)</returns>
    public void RandomReadFromFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RANDOM READ FROM FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        byte result = _fileControlBlockManager.ReadRecord(fcbAddress, true);
        State.AL = result;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Random Read result: AL={Result}", State.AL);
        }
    }

    /// <summary>
    /// Performs random write to an FCB (DOS function 0x22).
    /// DS:DX = pointer to opened FCB with RandomRecord field set
    /// </summary>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    /// <returns>AL = 0 (success), 1 (disk full), 2 (segment wrap)</returns>
    public void RandomWriteToFcb(bool calledFromVm) {
        uint fcbAddress = MemoryUtils.ToPhysicalAddress(State.DS, State.DX);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("RANDOM WRITE TO FCB at 0x{FcbAddress:X8}", fcbAddress);
        }
        
        byte result = _fileControlBlockManager.WriteRecord(fcbAddress, true);
        State.AL = result;
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FCB Random Write result: AL={Result}", State.AL);
        }
    }
}