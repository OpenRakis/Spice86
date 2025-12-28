namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Cmos;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Devices;
using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System;

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
    private readonly IOPortDispatcher _ioPortDispatcher;
    private readonly DosTables _dosTables;

    private byte _lastDisplayOutputCharacter = 0x0;
    private bool _isCtrlCFlag;
    
    private const ushort OffsetMask = 0x0F;

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
    /// <param name="dosProcessManager">The DOS class responsible for program loading and execution.</param>
    /// <param name="ioPortDispatcher">The I/O port dispatcher for accessing hardware ports (e.g., CMOS).</param>
    /// <param name="dosTables">The DOS tables structure containing CDS and DBCS information.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public DosInt21Handler(IMemory memory, DosProgramSegmentPrefixTracker dosPspTracker,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack, State state,
        KeyboardInt16Handler keyboardInt16Handler, CountryInfo countryInfo,
        DosStringDecoder dosStringDecoder, DosMemoryManager dosMemoryManager,
        DosFileManager dosFileManager, DosDriveManager dosDriveManager,
        DosProcessManager dosProcessManager,
        IOPortDispatcher ioPortDispatcher, DosTables dosTables, ILoggerService loggerService)
            : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _countryInfo = countryInfo;
        _dosPspTracker = dosPspTracker;
        _dosStringDecoder = dosStringDecoder;
        _keyboardInt16Handler = keyboardInt16Handler;
        _dosMemoryManager = dosMemoryManager;
        _dosFileManager = dosFileManager;
        _dosDriveManager = dosDriveManager;
        _dosProcessManager = dosProcessManager;
        _ioPortDispatcher = ioPortDispatcher;
        _dosTables = dosTables;
        _interruptVectorTable = new InterruptVectorTable(memory);
        FillDispatchTable();
    }

    /// <summary>
    /// Register the handlers for the DOS INT21H services that we support.
    /// </summary>
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
        AddAction(0x3D, () => OpenFileorDevice(true));
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
        AddAction(0x50, () => SetCurrentPsp());
        AddAction(0x4A, () => ModifyMemoryBlock(true));
        AddAction(0x4B, () => LoadAndOrExecute(true));
        AddAction(0x4C, QuitWithExitCode);
        AddAction(0x4D, GetReturnCode);
        AddAction(0x4E, () => FindFirstMatchingFile(true));
        AddAction(0x4F, () => FindNextMatchingFile(true));
        AddAction(0x51, GetPspAddress);
        AddAction(0x52, GetListOfLists);
        AddAction(0x62, GetPspAddress);
        AddAction(0x63, GetLeadByteTable);
        AddAction(0x66, () => GetSetGlobalLoadedCodePageTable(true));
    }

    public void SetDate() {
        ushort year = State.CX;
        byte month = State.DH;
        byte day = State.DL;

        bool valid = false;
        try {
            if (year >= 1980 && year <= 2099 && month >= 1 && month <= 12 && day >= 1) {
                _ = new DateTime(year, month, day);
                valid = true;
            }
        } catch (ArgumentOutOfRangeException) {
            valid = false;
        }

        if (valid) {
            int century = year / 100;
            int yearPart = year % 100;

            byte yearBcd = BcdConverter.ToBcd((byte)yearPart);
            byte monthBcd = BcdConverter.ToBcd(month);
            byte dayBcd = BcdConverter.ToBcd(day);
            byte centuryBcd = BcdConverter.ToBcd((byte)century);

            WriteCmosRegister(CmosRegisterAddresses.Year, yearBcd);
            WriteCmosRegister(CmosRegisterAddresses.Month, monthBcd);
            WriteCmosRegister(CmosRegisterAddresses.DayOfMonth, dayBcd);
            WriteCmosRegister(CmosRegisterAddresses.Century, centuryBcd);

            State.AL = 0;

            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("SET DOS DATE to CMOS: {Year}-{Month:D2}-{Day:D2}",
                    year, month, day);
            }
        } else {
            State.AL = 0xFF;

            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("SET DOS DATE called with invalid date: {Year}-{Month:D2}-{Day:D2}",
                    year, month, day);
            }
        }
    }

    public void SetTime() {
        byte hour = State.CH;
        byte minutes = State.CL;
        byte seconds = State.DH;
        byte hundredths = State.DL;

        bool valid = hour <= 23 &&
                     minutes <= 59 &&
                     seconds <= 59 &&
                     hundredths <= 99;

        if (valid) {
            byte hourBcd = BcdConverter.ToBcd(hour);
            byte minutesBcd = BcdConverter.ToBcd(minutes);
            byte secondsBcd = BcdConverter.ToBcd(seconds);

            WriteCmosRegister(CmosRegisterAddresses.Hours, hourBcd);
            WriteCmosRegister(CmosRegisterAddresses.Minutes, minutesBcd);
            WriteCmosRegister(CmosRegisterAddresses.Seconds, secondsBcd);

            State.AL = 0;

            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("SET DOS TIME to CMOS: {Hour:D2}:{Minutes:D2}:{Seconds:D2}.{Hundredths:D2}",
                    hour, minutes, seconds, hundredths);
            }
        } else {
            State.AL = 0xFF;

            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("SET DOS TIME called with invalid time: {Hour:D2}:{Minutes:D2}:{Seconds:D2}.{Hundredths:D2}",
                    hour, minutes, seconds, hundredths);
            }
        }
    }

    /// <summary>
    /// INT 21h, AH=63h - Get Double Byte Character Set (DBCS) Lead Byte Table.
    /// <para>
    /// Returns a pointer to the DBCS lead-byte table, which indicates which byte values
    /// are lead bytes in double-byte character sequences (e.g., Japanese, Chinese, Korean).
    /// An empty table (value 0) indicates no DBCS ranges are defined.
    /// </para>
    /// <b>Expects:</b><br/>
    /// AL = 0 to get DBCS lead byte table pointer
    /// <b>Returns:</b><br/>
    /// If AL was 0:<br/>
    /// - DS:SI = pointer to DBCS lead byte table<br/>
    /// - AL = 0<br/>
    /// - CF = 0 (undocumented)<br/>
    /// If AL was not 0:<br/>
    /// - AL = 0xFF<br/>
    /// </summary>
    private void GetLeadByteTable() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 21h AH=63h - Get DBCS Lead Byte Table, AL={AL:X2}", State.AL);
        }

        if (State.AL == 0) {
            uint dbcsAddress = _dosTables.DoubleByteCharacterSet.BaseAddress;
            ushort segment = MemoryUtils.ToSegment(dbcsAddress);
            ushort offset = (ushort)(dbcsAddress & OffsetMask);

            State.DS = segment;
            State.SI = offset;
            State.AL = 0;
            State.CarryFlag = false; // FreeDOS clears carry flag on success

            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("Returning DBCS table pointer at {Segment:X4}:{Offset:X4}", segment, offset);
            }
        } else {
            // FreeDOS returns error without modifying carry flag for invalid subfunction
            State.AL = 0xFF;
        }
    }

    /// <summary>
    /// Obtains or selects the current code page.
    /// </summary>
    /// <remarks>Setting the global loaded code page table is not supported and has no effect.</remarks>
    /// <param name="calledFromVm">Whether this was called by the emulator.</param>
    public void GetSetGlobalLoadedCodePageTable(bool calledFromVm) {
        if (State.AL == 1) {
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("Getting the global loaded code page is not supported - returned 0 which passes test programs...");
            }
            State.BX = State.DX = 0;
            SetCarryFlag(false, calledFromVm);
        } else if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning("Setting the global loaded code page is not supported.");
        }
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
    /// TODO: bugged! inline ASM maybe..does not WAIT for the keyboard...
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
    /// <param name="calledFromVm">Whether the method was called by the emulator.</param>
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
    /// Sets the current Program Segment Prefix (PSP) segment. Function 50H. <br/>
    /// </summary>
    /// <remarks>
    /// Input: BX = new PSP segment value. <br/>
    /// Used by (for example) Day of the Tentacle.
    /// </remarks>
    /// <returns>
    /// None.
    /// </returns>
    public void SetCurrentPsp() {
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("SET CURRENT PSP: {PspSegment}", ConvertUtils.ToHex16(State.BX));
        }
        _dosPspTracker.SetCurrentPspSegment(State.BX);
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
        byte yearBcd = ReadCmosRegister(CmosRegisterAddresses.Year);
        byte monthBcd = ReadCmosRegister(CmosRegisterAddresses.Month);
        byte dayBcd = ReadCmosRegister(CmosRegisterAddresses.DayOfMonth);
        byte dayOfWeekBcd = ReadCmosRegister(CmosRegisterAddresses.DayOfWeek);
        byte centuryBcd = ReadCmosRegister(CmosRegisterAddresses.Century);

        int year = BcdConverter.FromBcd(yearBcd);
        int century = BcdConverter.FromBcd(centuryBcd);
        int month = BcdConverter.FromBcd(monthBcd);
        int day = BcdConverter.FromBcd(dayBcd);
        int dayOfWeek = BcdConverter.FromBcd(dayOfWeekBcd);

        int fullYear = century * 100 + year;

        int dosDayOfWeek;
        if (dayOfWeek == 0) {
            dosDayOfWeek = 0;
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("CMOS DayOfWeek register returned 0 (invalid). Defaulting DOS day of week to Sunday (0).");
            }
        } else {
            dosDayOfWeek = dayOfWeek - 1;
        }

        State.CX = (ushort)fullYear;
        State.DH = (byte)month;
        State.DL = (byte)day;
        State.AL = (byte)dosDayOfWeek;

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET DOS DATE from CMOS: {Year}-{Month:D2}-{Day:D2} (day of week: {DayOfWeek})",
                fullYear, month, day, dosDayOfWeek);
        }
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
    /// INT 21h, AH=31h - Terminate and Stay Resident.
    /// Keeps the program in memory and returns control to the parent process.
    /// </summary>
    /// <remarks>
    /// Input: AL = return code, DX = number of paragraphs to keep resident
    /// </remarks>
    private void TerminateAndStayResident() {
        ushort paragraphsToKeep = State.DX;
        byte returnCode = State.AL;

        const ushort MinimumParagraphs = DosProgramSegmentPrefix.PspSizeInParagraphs;
        if (paragraphsToKeep < MinimumParagraphs) {
            paragraphsToKeep = MinimumParagraphs;
        }

        // Get the current PSP
        DosProgramSegmentPrefix? currentPsp = _dosPspTracker.GetCurrentPsp();
        ushort currentPspSegment = _dosPspTracker.GetCurrentPspSegment();

        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information(
                "TSR: Terminating with return code {ReturnCode}, keeping {Paragraphs} paragraphs at PSP {PspSegment:X4}",
                returnCode, paragraphsToKeep, currentPspSegment);
        }

        // Resize the memory block for the current PSP
        // The memory block starts at PSP segment, and we resize it to keep only the requested paragraphs
        DosErrorCode errorCode = _dosMemoryManager.TryModifyBlock(
            currentPspSegment,
            paragraphsToKeep,
            out DosMemoryControlBlock _);

        // Even if resize fails, we still terminate as a TSR
        // This matches FreeDOS behavior - it doesn't check the return value of DosMemChange
        if (errorCode != DosErrorCode.NoError && LoggerService.IsEnabled(LogEventLevel.Warning)) {
            LoggerService.Warning(
                "TSR: Failed to resize memory block to {Paragraphs} paragraphs, error: {Error}",
                paragraphsToKeep, errorCode);
        }

        // TSR terminates execution but keeps memory resident.
        // Unlike normal termination (AH=4Ch), TSR does NOT free the process memory.
        // However, TSR DOES pop the PSP from the tracker because the parent process
        // becomes the current process again. This allows the parent to make subsequent
        // EXEC calls without the stale TSR PSP being treated as current.
        _dosPspTracker.PopCurrentPspSegment();
        _dosProcessManager.TerminateProcess(0, DosTerminationType.TSR, _interruptVectorTable);
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
    /// INT 21h, AH=52h - Get List of Lists (SYSVARS).
    /// <para>
    /// Returns a pointer to the DOS internal tables (also known as the "List of Lists" or SYSVARS).
    /// </para>
    /// <remarks>
    /// Like FREEDOS and MS-DOS, this actually returns a pointer to the first drive DOS Parameter Block, which is the "official" start of the structure. <br/>
    /// This is used by (for example) the game 'Day of the Tentacle' and 'Indiana Jones and the Fate of Atlantis'.
    /// </remarks>
    /// </summary>
    private void GetListOfLists() {
        // Return pointer to the List of Lists (SYSVARS)
        // ES:BX points to offset 0 of the SYSVARS structure
        State.ES = DosSysVars.Segment;
        State.BX = 0;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose(
                "GET LIST OF LISTS ES:BX={Es}:{Bx}",
                ConvertUtils.ToHex16(State.ES),
                ConvertUtils.ToHex16(State.BX));
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
        byte hourBcd = ReadCmosRegister(CmosRegisterAddresses.Hours);
        byte minuteBcd = ReadCmosRegister(CmosRegisterAddresses.Minutes);
        byte secondBcd = ReadCmosRegister(CmosRegisterAddresses.Seconds);

        byte hour = BcdConverter.FromBcd(hourBcd);
        byte minute = BcdConverter.FromBcd(minuteBcd);
        byte second = BcdConverter.FromBcd(secondBcd);

        byte hundredths = 0;

        State.CH = hour;
        State.CL = minute;
        State.DH = second;
        State.DL = hundredths;

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET DOS TIME from CMOS: {Hour:D2}:{Minute:D2}:{Second:D2}.{Hundredths:D2}",
                hour, minute, second, hundredths);
        }
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
    /// INT 21h, AH=4Bh - EXEC: Load and/or Execute Program.
    /// </summary>
    /// <param name="calledFromVm">Whether the code was called by the emulator.</param>
    public void LoadAndOrExecute(bool calledFromVm) {
        string programName = _dosStringDecoder.GetZeroTerminatedStringAtDsDx();
        DosExecLoadType loadType = (DosExecLoadType)State.AL;
        uint paramBlockAddress = MemoryUtils.ToPhysicalAddress(State.ES, State.BX);
        DosExecResult result;

        if (loadType == DosExecLoadType.LoadOverlay) {
            DosExecOverlayParameterBlock overlayParamBlock = new(Memory, paramBlockAddress);
            result = _dosProcessManager.LoadOverlay(programName, overlayParamBlock.LoadSegment, overlayParamBlock.RelocationFactor);
        } else {
            DosExecParameterBlock paramBlock = new(Memory, paramBlockAddress);
            uint cmdTailAddress = MemoryUtils.ToPhysicalAddress(paramBlock.CommandTailSegment, paramBlock.CommandTailOffset);
            DosCommandTail cmdTail = new(Memory, cmdTailAddress);
            string commandTail = cmdTail.Length > 0 ? cmdTail.Command.TrimEnd('\r') : string.Empty;
            result = _dosProcessManager.LoadOrLoadAndExecute(programName, paramBlock, commandTail, loadType, paramBlock.EnvironmentSegment, _interruptVectorTable);
        }
        HandleDosExecResult(calledFromVm, result);
    }

    private void HandleDosExecResult(bool calledFromVm, DosExecResult result) {
        if (result.Success) {
            SetCarryFlag(false, calledFromVm);
        } else {
            SetCarryFlag(true, calledFromVm);
            State.AX = (ushort)result.ErrorCode;
            LogDosError(calledFromVm);
        }
    }

    public DosExecResult LoadOverlay(string programName, DosExecOverlayParameterBlock overlayParamBlock) {
        DosExecResult result = _dosProcessManager.LoadOverlay(programName, overlayParamBlock.LoadSegment, overlayParamBlock.RelocationFactor);
        HandleDosExecResult(calledFromVm: true, result);
        return result;
    }

    public DosExecResult LoadOnly(string programName, DosExecParameterBlock paramBlock, string? commandTail = "") {
        DosExecResult result = _dosProcessManager.LoadOrLoadAndExecute(programName, paramBlock, commandTail ?? "", DosExecLoadType.LoadOnly, paramBlock.EnvironmentSegment, _interruptVectorTable);
        HandleDosExecResult(calledFromVm: true, result);
        return result;
    }

    public DosExecResult LoadAndExecute(string programName, DosExecParameterBlock paramBlock, string? commandTail = "") {
        DosExecResult result = _dosProcessManager.LoadOrLoadAndExecute(programName, paramBlock, commandTail ?? "", DosExecLoadType.LoadAndExecute, paramBlock.EnvironmentSegment, _interruptVectorTable);
        HandleDosExecResult(calledFromVm: true, result);
        return result;
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
        int offset = (State.CX << 16) | State.DX;
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
    public void OpenFileorDevice(bool calledFromVm) {
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
    /// Quits the current DOS process and sets the exit code from the value in the AL register. <br/>
    /// If this is a child process, restores the parent's state. Otherwise, terminates the emulator.
    /// </summary>
    public void QuitWithExitCode() {
        byte exitCode = State.AL;
        if (LoggerService.IsEnabled(LogEventLevel.Information)) {
            LoggerService.Information("INT21H AH=4Ch: TERMINATE with exit code {ExitCode:X2}", exitCode);
        }

        bool shouldContinue = _dosProcessManager.TerminateProcess(
            exitCode,
            DosTerminationType.Normal,
            _interruptVectorTable);

        if (!shouldContinue) {
            // No parent to return to - stop emulation
            State.IsRunning = false;
        }
        // If shouldContinue is true, TerminateProcess has set CS:IP (with -4 adjustment)
        // to the parent's return address. MoveIpAndSetNextNode will add 4 after this
        // handler returns, and execution will continue at the parent's correct address.
    }

    /// <summary>
    /// INT 21h, AH=4Dh - Get Return Code of Subprogram (WAIT).
    /// Returns the exit code of a terminated child process.
    /// </summary>
    /// <remarks>
    /// Returns:
    /// AH = termination type (00h = normal, 01h = Ctrl-C, 02h = critical error, 03h = TSR)
    /// AL = return code
    /// </remarks>
    public void GetReturnCode() {
        ushort returnCode = _dosProcessManager.GetLastChildExitCode();
        State.AX = returnCode;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("GET RETURN CODE: AX={Ax:X4}", returnCode);
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
    /// Reads a value from a CMOS register via I/O ports.
    /// Writes the register index to the address port and reads the value from the data port.
    /// </summary>
    private byte ReadCmosRegister(byte register) {
        _ioPortDispatcher.WriteByte(CmosPorts.Address, register);
        return _ioPortDispatcher.ReadByte(CmosPorts.Data);
    }

    /// <summary>
    /// Writes a value to a CMOS register via I/O ports.
    /// Writes the register index to the address port and the value to the data port.
    /// </summary>
    private void WriteCmosRegister(byte register, byte value) {
        _ioPortDispatcher.WriteByte(CmosPorts.Address, register);
        _ioPortDispatcher.WriteByte(CmosPorts.Data, value);
    }
}