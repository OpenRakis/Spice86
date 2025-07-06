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
using System.Text;

/// <summary>
/// Implementation of the DOS INT21H services.
/// </summary>
public class DosInt21Handler : InterruptHandler {
    private readonly DosMemoryManager _dosMemoryManager;
    private readonly DosDriveManager _dosDriveManager;
    private readonly InterruptVectorTable _interruptVectorTable;
    private readonly DosFileManager _dosFileManager;
    private readonly KeyboardInt16Handler _keyboardInt16Handler;
    private readonly DosStringDecoder _dosStringDecoder;
    private readonly CountryInfo _countryInfo;
    private readonly ConsoleControl _consoleControl;

    private byte _lastDisplayOutputCharacter = 0x0;
    private bool _isCtrlCFlag;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The emulator memory.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="keyboardInt16Handler">The keyboard interrupt handler.</param>
    /// <param name="countryInfo">The DOS kernel's global region settings.</param>
    /// <param name="dosStringDecoder">The helper class used to encode/decode DOS strings.</param>
    /// <param name="dosMemoryManager">The DOS class used to manage DOS MCBs.</param>
    /// <param name="dosFileManager">The DOS class responsible for DOS drive access.</param>
    /// <param name="dosDriveManager">The DOS class responsible for file and device-as-file access.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt21Handler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        KeyboardInt16Handler keyboardInt16Handler, CountryInfo countryInfo,
        DosStringDecoder dosStringDecoder, DosMemoryManager dosMemoryManager,
        DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        ConsoleControl consoleControl, ILoggerService loggerService)
            : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _consoleControl = consoleControl;
        _countryInfo = countryInfo;
        _dosStringDecoder = dosStringDecoder;
        _keyboardInt16Handler = keyboardInt16Handler;
        _dosMemoryManager = dosMemoryManager;
        _dosFileManager = dosFileManager;
        _dosDriveManager = dosDriveManager;
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
        AddAction(0x2A, GetDate);
        AddAction(0x2C, GetTime);
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
        AddAction(0x3D, () => OpenFile(true));
        AddAction(0x3E, () => CloseFile(true));
        AddAction(0x3F, () => ReadFromFileOrDevice(true));
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
        if (!_dosFileManager.TryGetStandardInput(out CharacterDevice? stdIn) ||
            !stdIn.CanRead) {
            State.AL = 0;
        } else {
            _consoleControl.Echo = false;
            byte[] bytes = new byte[1];
            var readCount = stdIn.Read(bytes, 0, 1);
            if (readCount < 1) {
                State.AL = 0;
            } else {
                State.AL = bytes[0];
            }
            _consoleControl.Echo = true;
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
    public void CloseFile(bool calledFromVm) {
        ushort fileHandle = State.BX;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("CLOSE FILE handle {FileHandle}", ConvertUtils.ToHex(fileHandle));
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.CloseFile(
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
        bool echo = _consoleControl.Echo;
        _consoleControl.Echo = true; // Enable echoing to the standard output.
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
        _consoleControl.Echo = echo;
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
                _consoleControl.DirectOutput = true;
                stdOut.Write(character);
                _consoleControl.DirectOutput = false;
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
        ushort attributes = State.CX;
        string fileSpec = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("FIND NEXT MATCHING FILE {Attributes}, {FileSpec}",
                ConvertUtils.ToHex16(attributes), fileSpec);
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
                blockSegment);
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
    /// Terminate the current process, and either prepare unloading it, or keep it in memory.
    /// </summary>
    /// <exception cref="NotImplementedException">TSR Support is not implemented</exception>
    private void TerminateAndStayResident() {
        throw new NotImplementedException("TSR Support is not implemented");
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
        ushort pspSegment = _dosMemoryManager.PspSegment;
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
    /// Possible error code in AX: 0x08 (Insufficient memory). <br/>
    /// On success, AX is set to the size of the MCB, which is at least equal to the requested size.
    /// </returns>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    public void ModifyMemoryBlock(bool calledFromVm) {
        ushort requestedSize = State.BX;
        ushort blockSegment = State.ES;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("MODIFY MEMORY BLOCK {Size}, {BlockSegment}",
                requestedSize, blockSegment);
        }
        if (_dosMemoryManager.TryModifyBlock(blockSegment, ref requestedSize,
            out DosMemoryControlBlock? modifiedBlock)) {
            State.AX = requestedSize;
            SetCarryFlag(false, calledFromVm);
        } else {
            LogDosError(calledFromVm);
            // An error occurred. Report it as not enough memory.
            SetCarryFlag(true, calledFromVm);
            State.AX = 0x08;
            State.BX = requestedSize;
        }
    }

    /// <summary>
    /// Either only load a program or overlay, or load it and run it.
    /// </summary>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    /// <exception cref="NotImplementedException">This function is not implemented</exception>
    public void LoadAndOrExecute(bool calledFromVm) {
        string programName = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
        throw new NotImplementedException($"INT21H: load and/or execute program is not implemented. Emulated program tried to load and/or exec: {programName}");
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
    public void OpenFile(bool calledFromVm) {
        string fileName = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
        byte accessMode = State.AL;
        FileAccessMode fileAccessMode = (FileAccessMode)(accessMode & 0b111);
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("OPEN FILE {FileName} with mode {AccessMode} : {FileAccessModeByte}", 
                fileName, fileAccessMode,
                ConvertUtils.ToHex8(State.AL));
        }
        DosFileOperationResult dosFileOperationResult = _dosFileManager.OpenFile(
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
    /// Quits the current DOS process and sets the exit code from the value in the AL register. <br/>
    /// TODO: This is only a stub that sets the cpu state <see cref="State.IsRunning"/> property to <c>False</c>, thus ending the emulation loop !
    /// </summary>
    public void QuitWithExitCode() {
        byte exitCode = State.AL;
        if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning("INT21H: QUIT WITH EXIT CODE {ExitCode}", ConvertUtils.ToHex8(exitCode));
        }
        State.IsRunning = false;
    }

    /// <summary>
    /// Reads a file or device from the file handle in BX, the read length in CX, and the buffer at DS:DX.
    /// </summary>
    /// <returns>
    /// CF is cleared on success. <br/>
    /// CF is set on error.
    /// </returns>
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
    public void ReadFromFileOrDevice(bool calledFromVm) {
        _consoleControl.Echo = false;
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
        _consoleControl.Echo = true;
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
        if(_dosDriveManager.TryGetValue(DosDriveManager.DriveLetters.ElementAtOrDefault(State.DL).Key, out VirtualDrive? mountedDrive)) {
            _dosDriveManager.CurrentDrive = mountedDrive;
        } 
        if (State.DL > DosDriveManager.MaxDriveCount && LoggerService.IsEnabled(LogEventLevel.Error)) {
            LoggerService.Error("DOS INT21H: Could not set default drive! Unrecognized index in State.DL: {DriveIndex}", State.DL);
        }
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SELECT DEFAULT DRIVE {@DefaultDrive}", _dosDriveManager.CurrentDrive);
        }
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
    /// Provides MS-DOS drivers based IOCTL operations, such as: get device information, set device information, get logical drive for physical drive... <br/>
    /// <para>
    /// AL = 0: Get device information from the device handle in BX. Returns result in DX. TODO: Implement it entirely. <br/>
    /// AL = 1: Set device information. Does nothing. TODO: Implement it. <br/>
    /// AL = 0xE: Get logical drive for physical drive. Always returns 0 in AL for only one drive.
    /// </para>
    /// </summary>
    /// <remarks>
    ///  TODO: Update it once we mount more than just C: !
    ///  </remarks>
    /// <returns>
    /// Always indicates success by clearing the carry flag.
    /// </returns>
    /// <param name="calledFromVm">Whether this was called from internal emulator code.</param>
    /// <exception cref="UnhandledOperationException">When the IO control operation in the AL Register is not recognized.</exception>
    public void IoControl(bool calledFromVm) {
        DosFileOperationResult result = _dosFileManager.IoControl(State);
        SetStateFromDosFileOperationResult(calledFromVm, result);
    }

    private void LogDosError(bool calledFromVm) {
        string returnMessage = "";
        if (calledFromVm) {
            returnMessage = $"Int will return to {FunctionHandlerProvider.FunctionHandlerInUse.PeekReturn()}. ";
        }
        if (LoggerService.IsEnabled(LogEventLevel.Error)) {
            LoggerService.Error("DOS operation failed with an error. {ReturnMessage}. State is {State}",
                returnMessage, State.ToString());
        }
    }

    private void SetStateFromDosFileOperationResult(bool calledFromVm,
        DosFileOperationResult dosFileOperationResult) {
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